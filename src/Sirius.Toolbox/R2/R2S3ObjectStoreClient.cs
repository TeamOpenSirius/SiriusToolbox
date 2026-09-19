using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Sirius.AssetTool.R2;

internal sealed class R2S3ObjectStoreClient : IDisposable
{
    private const string Region = "auto";
    private const string Service = "s3";
    private const string EmptyPayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private readonly HttpClient _http;
    private readonly Uri _bucketBaseUri;
    private readonly string _accessKeyId;
    private readonly string _secretAccessKey;
    private readonly string? _sessionToken;

    public R2S3ObjectStoreClient(
        string endpointValue,
        string bucketValue,
        string accessKeyId,
        string secretAccessKey,
        string? sessionToken)
    {
        if (!Uri.TryCreate(endpointValue?.Trim(), UriKind.Absolute, out var endpoint) ||
            (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("R2 地址必须是 HTTP(S) 绝对地址。", nameof(endpointValue));
        }
        if (string.IsNullOrWhiteSpace(bucketValue))
            throw new ArgumentException("R2 存储桶不能为空。", nameof(bucketValue));
        if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(secretAccessKey))
            throw new InvalidOperationException("R2 访问密钥不完整。");

        _accessKeyId = accessKeyId.Trim();
        _secretAccessKey = secretAccessKey.Trim();
        _sessionToken = string.IsNullOrWhiteSpace(sessionToken) ? null : sessionToken.Trim();
        _bucketBaseUri = BuildBucketBaseUri(endpoint, bucketValue.Trim());

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            EnableMultipleHttp2Connections = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };
        _http = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sirius.ToolboxUI-R2/1.0");
    }

    public async Task<bool> IsCurrentAsync(
        string objectKey,
        long localLength,
        string sha256,
        string? contentEncoding,
        CancellationToken cancellationToken)
    {
        using var request = CreateSignedRequest(
            HttpMethod.Head,
            objectKey,
            EmptyPayloadHash,
            metadataSha256: null);
        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;
        await EnsureSuccessAsync(response, objectKey, cancellationToken);

        var remoteLength = response.Content.Headers.ContentLength;
        var remoteHash = response.Headers.TryGetValues("x-amz-meta-sha256", out var values)
            ? values.FirstOrDefault()
            : null;
        var remoteEncodings = response.Content.Headers.ContentEncoding;
        var contentEncodingMatches = string.IsNullOrWhiteSpace(contentEncoding)
            ? remoteEncodings.Count == 0
            : remoteEncodings.Any(item => string.Equals(item, contentEncoding, StringComparison.OrdinalIgnoreCase));
        return remoteLength == localLength &&
               string.Equals(remoteHash, sha256, StringComparison.OrdinalIgnoreCase) &&
               contentEncodingMatches;
    }

    public Task<bool> IsCurrentAsync(
        string objectKey,
        long localLength,
        string sha256,
        CancellationToken cancellationToken)
        => IsCurrentAsync(objectKey, localLength, sha256, null, cancellationToken);

    public async Task PutAsync(
        MasterDataR2UploadPlan plan,
        string sha256,
        CancellationToken cancellationToken)
        => await PutAsync(
            new R2UploadEntry(plan.DatabasePath, plan.ObjectKey, plan.Length, plan.ContentType, null),
            sha256,
            cancellationToken);

    public async Task PutAsync(
        R2UploadEntry entry,
        string sha256,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            entry.LocalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var content = new StreamContent(stream, 1024 * 1024);
        content.Headers.ContentLength = entry.Length;
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(entry.ContentType);
        if (!string.IsNullOrWhiteSpace(entry.ContentEncoding))
            content.Headers.ContentEncoding.Add(entry.ContentEncoding);

        using var request = CreateSignedRequest(
            HttpMethod.Put,
            entry.ObjectKey,
            sha256,
            sha256);
        request.Content = content;
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromDays(365)
        };
        request.Headers.ExpectContinue = false;

        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await EnsureSuccessAsync(response, entry.ObjectKey, cancellationToken);
    }

    private HttpRequestMessage CreateSignedRequest(
        HttpMethod method,
        string objectKey,
        string payloadHash,
        string? metadataSha256)
    {
        var uri = BuildObjectUri(objectKey);
        var now = DateTimeOffset.UtcNow;
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var host = uri.IsDefaultPort ? uri.Host : uri.Authority;

        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = host,
            ["x-amz-content-sha256"] = payloadHash,
            ["x-amz-date"] = amzDate
        };
        if (metadataSha256 is not null)
            headers["x-amz-meta-sha256"] = metadataSha256;
        if (_sessionToken is not null)
            headers["x-amz-security-token"] = _sessionToken;

        var canonicalHeaders = string.Concat(headers.Select(item => $"{item.Key}:{NormalizeHeader(item.Value)}\n"));
        var signedHeaders = string.Join(';', headers.Keys);
        var canonicalRequest = string.Join('\n',
            method.Method,
            CanonicalPath(uri),
            string.Empty,
            canonicalHeaders,
            signedHeaders,
            payloadHash);
        var credentialScope = $"{date}/{Region}/{Service}/aws4_request";
        var stringToSign = string.Join('\n',
            "AWS4-HMAC-SHA256",
            amzDate,
            credentialScope,
            Sha256Hex(canonicalRequest));
        var signature = Convert.ToHexString(
                Hmac(SigningKey(date), Encoding.UTF8.GetBytes(stringToSign)))
            .ToLowerInvariant();

        var request = new HttpRequestMessage(method, uri);
        request.Headers.Host = host;
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        if (metadataSha256 is not null)
            request.Headers.TryAddWithoutValidation("x-amz-meta-sha256", metadataSha256);
        if (_sessionToken is not null)
            request.Headers.TryAddWithoutValidation("x-amz-security-token", _sessionToken);
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            $"AWS4-HMAC-SHA256 Credential={_accessKeyId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}");
        return request;
    }

    private Uri BuildObjectUri(string objectKey)
    {
        var segments = objectKey.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(static segment => segment is "." or ".."))
            throw new InvalidDataException($"R2 对象键无效：{objectKey}");
        var escaped = string.Join('/', segments.Select(Uri.EscapeDataString));
        return new Uri(_bucketBaseUri, escaped);
    }

    private static Uri BuildBucketBaseUri(Uri endpoint, string bucket)
    {
        var segments = endpoint.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToList();
        if (segments.Count == 0 || !string.Equals(segments[^1], bucket, StringComparison.Ordinal))
            segments.Add(bucket);
        var path = string.Join('/', segments.Select(Uri.EscapeDataString));
        var builder = new UriBuilder(endpoint.Scheme, endpoint.Host, endpoint.IsDefaultPort ? -1 : endpoint.Port)
        {
            Path = $"/{path}/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    private byte[] SigningKey(string date)
    {
        var dateKey = Hmac(Encoding.UTF8.GetBytes($"AWS4{_secretAccessKey}"), Encoding.UTF8.GetBytes(date));
        var regionKey = Hmac(dateKey, Encoding.UTF8.GetBytes(Region));
        var serviceKey = Hmac(regionKey, Encoding.UTF8.GetBytes(Service));
        return Hmac(serviceKey, Encoding.UTF8.GetBytes("aws4_request"));
    }

    private static string CanonicalPath(Uri uri) =>
        string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;

    private static string NormalizeHeader(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static byte[] Hmac(byte[] key, byte[] value) => HMACSHA256.HashData(key, value);

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string objectKey,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 2048)
            body = body[..2048];
        throw new HttpRequestException(
            $"R2 请求失败（{objectKey}）：{(int)response.StatusCode} {response.ReasonPhrase}；{body}",
            null,
            response.StatusCode);
    }

    public void Dispose() => _http.Dispose();
}
