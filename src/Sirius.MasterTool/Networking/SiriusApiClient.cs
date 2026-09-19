using System.Net.Http.Headers;
using Sirius.Toolbox.Master.Protocol;
using Sirius.Toolbox.Master.Serialization;

namespace Sirius.MasterTool;

internal sealed class SiriusApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly DownloaderOptions _options;
    private string? _bearerToken;
    private string? _assetVersion;
    private string? _masterDataVersion;

    public SiriusApiClient(DownloaderOptions options)
    {
        _options = options;
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            EnableMultipleHttp2Connections = true
        };
        if (options.InsecureTls)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("BestHTTP/2 v2.8.5");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.msgpack"));
        _bearerToken = options.AccessToken;
    }

    public void SetBearerToken(string? token) => _bearerToken = token;

    public void SetAssetVersion(string? assetVersion) => _assetVersion = assetVersion;

    public void SetMasterDataVersion(string? masterDataVersion) => _masterDataVersion = masterDataVersion;

    public Task<EnvironmentResult> GetEnvironmentAsync(CancellationToken ct)
    {
        var query = $"applicationVersion={Uri.EscapeDataString(_options.ApplicationVersion)}&gameVersion={_options.GameVersion}";
        return SendAsync<object, EnvironmentResult>(HttpMethod.Post,
            Combine(_options.ApiBootstrapUrl, "/api/Environment") + "?" + query, null, false, ct);
    }

    public Task<AccountRegistResult> RegisterAsync(string apiBase, string name, CancellationToken ct)
        => SendAsync<RegisterPayload, AccountRegistResult>(HttpMethod.Post,
            Combine(apiBase, "/api/Account/Register"), new RegisterPayload { Name = name }, false, ct);

    public Task<AuthenticateResult> AuthenticateAsync(string apiBase, string loginToken, CancellationToken ct)
        => SendAsync<AuthenticatePayload, AuthenticateResult>(HttpMethod.Post,
            Combine(apiBase, "/api/Account/Authenticate"), new AuthenticatePayload
            {
                LoginToken = loginToken,
                GameVersion = (GameVersions)_options.GameVersion,
                ApkHash = "A5CD6E6681BCC137FB6963B3A421F68BD153A1FA:" +
                          "1EFF46A116B8A6B91E858B34D722C18B54F64132:" +
                          "8D695CF32507DD218633ABAF429370A259519AEA",
                ApkApplicationSignature = "A40DA80A59D170CAA950CF15C18C454D47A39B26989D8B640ECD745BA71BF5DC",
                ApplicationVersion = _options.AuthenticationApplicationVersion
            }, false, ct);

    public Task<LoginResult> LoginAsync(string apiBase, CancellationToken ct)
        => SendAsync<LoginPayload, LoginResult>(HttpMethod.Post,
            Combine(apiBase, "/api/Login"), new LoginPayload { PushNotificationToken = string.Empty }, true, ct);

    public Task<MasterDataManifest> GetMasterManifestAsync(string apiBase, CancellationToken ct)
        => SendAsync<object, MasterDataManifest>(HttpMethod.Get,
            Combine(apiBase, "/api/data/master"), null, true, ct);

    public async Task DownloadFileAsync(Uri uri, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + ".part";
        var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0L;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (existing > 0 && response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            File.Delete(partial);
            existing = 0;
        }
        else if (existing > 0
                 && response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            var remoteLength = response.Content.Headers.ContentRange?.Length;
            if (remoteLength is null)
            {
                using var headRequest = new HttpRequestMessage(HttpMethod.Head, uri);
                using var headResponse = await _http.SendAsync(
                    headRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                if (headResponse.IsSuccessStatusCode)
                    remoteLength = headResponse.Content.Headers.ContentLength;
            }

            if (remoteLength == existing)
            {
                File.Move(partial, destination, true);
                return;
            }

            response.Dispose();
            File.Delete(partial);
            await DownloadFileAsync(uri, destination, ct);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Download {uri} failed: {(int)response.StatusCode} {response.ReasonPhrase}; body={errorBody}");
        }

        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var target = new FileStream(partial, existing > 0 ? FileMode.Append : FileMode.Create,
                         FileAccess.Write, FileShare.None, 1024 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await source.CopyToAsync(target, 1024 * 1024, ct);
            await target.FlushAsync(ct);
        }

        File.Move(partial, destination, true);
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(HttpMethod method, string url,
        TRequest? payload, bool authenticated, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        ApplyHeaders(request, authenticated);
        if (payload is not null && method != HttpMethod.Get)
        {
            var bytes = ApiStreamCodec.EncodeRequest(payload);
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.msgpack");
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        var body = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"{method} {url} failed: {(int)response.StatusCode} {response.ReasonPhrase}; body={Convert.ToHexString(body.AsSpan(0, Math.Min(body.Length, 128)))}",
                null,
                response.StatusCode);
        return ApiStreamCodec.DecodePayload<TResponse>(body);
    }

    private void ApplyHeaders(HttpRequestMessage request, bool authenticated)
    {
        request.Headers.TryAddWithoutValidation("X-Platform", _options.Platform);
        request.Headers.TryAddWithoutValidation("X-FM", _options.Fm);
        request.Headers.TryAddWithoutValidation("X-Game-Version", _options.GameVersion.ToString());
        request.Headers.TryAddWithoutValidation("X-Client-Version", _options.AuthenticationApplicationVersion);
        if (!string.IsNullOrWhiteSpace(_assetVersion))
            request.Headers.TryAddWithoutValidation("X-Asset-Version", _assetVersion);
        if (!string.IsNullOrWhiteSpace(_masterDataVersion))
            request.Headers.TryAddWithoutValidation("X-MasterData-Version", _masterDataVersion);
        if (authenticated && !string.IsNullOrWhiteSpace(_bearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
    }

    private static string Combine(string baseUrl, string path)
        => baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');

    public void Dispose() => _http.Dispose();
}
