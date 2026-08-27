using System.Security.Cryptography;
using System.Text.Json;
using Sirius.Tooling.Core.Master.Export;
using Sirius.Tooling.Core.Master.Persistence;
using Sirius.Tooling.Core.Master.Protocol;

namespace Sirius.MasterTool;

internal static class Program
{
    private const string MasterJsonExportFormat = "object-schema-v1";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = Cli.Parse(args);
            if (options is null)
                return 0;

            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
            await RunAsync(options, shutdown.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task RunAsync(DownloaderOptions options, CancellationToken ct)
    {
        var root = Path.GetFullPath(options.OutputDirectory);
        var masterDir = Path.Combine(root, "master");
        var statePath = Path.Combine(root, "state.json");
        var publicationPath = Path.Combine(root, "publication.json");
        Directory.CreateDirectory(masterDir);

        var state = await StateStore.LoadAsync(statePath, ct);
        using var api = new SiriusApiClient(options);

        Console.WriteLine("Fetching environment...");
        var environment = await api.GetEnvironmentAsync(ct);
        var apiBase = string.IsNullOrWhiteSpace(environment.ApiEndpoint)
            ? options.ApiBootstrapUrl
            : environment.ApiEndpoint;
        api.SetAssetVersion(environment.AssetVersion);

        var cachedTokenMatchesClient = state.GameVersion == options.GameVersion
            && string.Equals(state.AuthenticationApplicationVersion,
                options.AuthenticationApplicationVersion, StringComparison.Ordinal);

        state.ApiEndpoint = apiBase;
        state.MasterDataUrl = environment.MasterDataUrl;
        state.AssetUrl = environment.AssetUrl;
        state.StaticContentUrl = environment.StaticContentUrl;
        state.PhotoContentUrl = environment.PhotoContentUrl;
        state.AssetVersion = environment.AssetVersion;
        state.ApplicationVersion = environment.ApplicationVersion;
        state.AuthenticationApplicationVersion = options.AuthenticationApplicationVersion;
        state.GameVersion = options.GameVersion;

        var accessToken = options.AccessToken
            ?? (cachedTokenMatchesClient ? state.AccessToken : null);
        var loginToken = options.LoginToken ?? state.LoginToken;

        if (options.AccessToken is null && !cachedTokenMatchesClient && !string.IsNullOrWhiteSpace(state.AccessToken))
            Console.WriteLine("Cached access token was created for different client parameters; re-authenticating...");

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            if (string.IsNullOrWhiteSpace(loginToken))
            {
                Console.WriteLine("No LoginToken found; registering a new archive account...");
                var registration = await api.RegisterAsync(apiBase, options.RegistrationName, ct);
                loginToken = registration.Token;
                if (string.IsNullOrWhiteSpace(loginToken))
                    throw new InvalidDataException($"Registration returned no token; error={registration.ErrorType}.");
                state.LoginToken = loginToken;
                state.UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await StateStore.SaveAsync(statePath, state, ct);
                Console.WriteLine("Registration succeeded.");
            }

            Console.WriteLine("Authenticating...");
            var authentication = await api.AuthenticateAsync(apiBase, loginToken, ct);
            accessToken = authentication.Token;
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new InvalidDataException("Authenticate returned an empty access token.");
            state.LoginToken = loginToken;
            state.AccessToken = accessToken;
            state.UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await StateStore.SaveAsync(statePath, state, ct);
        }

        api.SetBearerToken(accessToken);
        Console.WriteLine("Logging in...");
        try
        {
            await api.LoginAsync(apiBase, ct);
        }
        catch (HttpRequestException ex) when ((int?)ex.StatusCode == 440
                                              && !string.IsNullOrWhiteSpace(loginToken))
        {
            // Official access/session tokens expire while the long-lived login token
            // remains valid. Re-authenticate once, persist the replacement, then retry.
            Console.WriteLine("Cached access token expired (HTTP 440); re-authenticating...");
            api.SetBearerToken(null);
            state.AccessToken = null;

            var authentication = await api.AuthenticateAsync(apiBase, loginToken, ct);
            accessToken = authentication.Token;
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new InvalidDataException("Re-authentication returned an empty access token.");

            api.SetBearerToken(accessToken);
            state.AccessToken = accessToken;
            state.LoginToken = loginToken;
            state.UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await StateStore.SaveAsync(statePath, state, ct);

            Console.WriteLine("Re-authentication succeeded; retrying login...");
            await api.LoginAsync(apiBase, ct);
        }
        LocalMasterDataMetadata? currentMasterData = null;
        {
            Console.WriteLine("Fetching master-data manifest...");
            var manifest = await api.GetMasterManifestAsync(apiBase, ct);
            api.SetMasterDataVersion(manifest.Version);

            state.LoginToken = loginToken;
            state.AccessToken = accessToken;
            state.UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var databasePath = Path.Combine(masterDir, "mastermemory.db");
            var masterManifestPath = Path.Combine(masterDir, "manifest.json");
            var localMasterVersion = state.MasterDataVersion
                ?? await ReadLocalMasterVersionAsync(masterManifestPath, ct);
            var masterCurrent = !options.Force
                && string.Equals(localMasterVersion, manifest.Version, StringComparison.Ordinal)
                && File.Exists(databasePath);

            if (masterCurrent)
            {
                Console.WriteLine($"Master data {manifest.Version} already exists; skipping download.");
            }
            else
            {
                var masterUri = BuildContentUri(environment.MasterDataUrl, manifest.Uri, manifest.SasToken);
                Console.WriteLine($"Downloading {masterUri}");
                if (File.Exists(databasePath))
                    File.Copy(databasePath, databasePath + ".bck", overwrite: true);
                await api.DownloadFileAsync(masterUri, databasePath, ct);
            }

            currentMasterData = await CreateLocalMasterDataMetadataAsync(
                manifest,
                databasePath,
                ct);
            await WriteMasterManifestAsync(
                masterManifestPath,
                currentMasterData,
                environment,
                databasePath,
                ct);

            state.MasterDataVersion = manifest.Version;
            state.UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await StateStore.SaveAsync(statePath, state, ct);

            if (options.ExportJson && File.Exists(databasePath))
            {
                var jsonDirectory = Path.Combine(masterDir, "json");
                var exportMarkerPath = Path.Combine(jsonDirectory, ".complete");
                var expectedExportMarker =
                    $"{MasterJsonExportFormat}:{currentMasterData.Version}";
                var exportCurrent = !options.Force
                    && string.Equals(await ReadTextIfExistsAsync(exportMarkerPath, ct),
                        expectedExportMarker, StringComparison.Ordinal);
                if (exportCurrent)
                {
                    Console.WriteLine($"Master JSON {manifest.Version} already exported; skipping parse.");
                }
                else
                {
                    var schemaPath = ResolveTableSchemaPath(options.TableSchemaPath);
                    if (schemaPath is null)
                        throw new FileNotFoundException(
                            "Master table schema was not found. Specify --table-schema or deploy data/table.json.");
                    Console.WriteLine("Exporting master tables to JSON...");
                    var exporter = new MasterMemoryExporter(schemaPath);
                    await exporter.ExportAsync(databasePath, jsonDirectory, ct);
                    await File.WriteAllTextAsync(exportMarkerPath, expectedExportMarker, ct);
                }

                state.MasterJsonVersion = currentMasterData.Version;

                state.UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await StateStore.SaveAsync(statePath, state, ct);
            }

            await WritePublicationAsync(
                publicationPath,
                root,
                masterDir,
                options,
                state,
                environment,
                currentMasterData,
                ct);
        }

        state.LoginToken = loginToken;
        state.AccessToken = accessToken;
        state.UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await StateStore.SaveAsync(statePath, state, ct);
        Console.WriteLine("Done.");
    }

    private static string? ResolveTableSchemaPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullPath = Path.GetFullPath(configuredPath);
            return File.Exists(fullPath) ? fullPath : null;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "data", "table.json"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "data", "table.json"))
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task WritePublicationAsync(
        string publicationPath,
        string root,
        string masterDirectory,
        DownloaderOptions options,
        DownloadState state,
        EnvironmentResult environment,
        LocalMasterDataMetadata masterData,
        CancellationToken cancellationToken)
    {
        var manifestDirectory = Path.Combine(root, "assets", "manifests");
        var cdnManifest = Directory.Exists(manifestDirectory)
            ? Directory.EnumerateFiles(manifestDirectory, "cdn_*.json", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName
            : null;
        var publication = new MasterSyncPublication
        {
            MasterDataVersion = masterData.Version,
            SourceMasterDataVersion = masterData.Version,
            MasterDataPublishTimestamp = masterData.PublishTimestamp,
            MasterDataUri = masterData.Uri,
            MasterDataFile = Path.Combine(masterDirectory, "mastermemory.db"),
            MasterDataSha256 = masterData.Sha256,
            MasterDataPolicy = string.Empty,
            MasterJsonDirectory = Path.Combine(masterDirectory, "json"),
            MasterIndexDatabase = string.Empty,
            AssetVersion = environment.AssetVersion,
            AssetSourceUrl = environment.AssetUrl,
            StaticContentSourceUrl = environment.StaticContentUrl,
            CdnManifest = cdnManifest,
            PublishedAt = DateTimeOffset.UtcNow
        };
        await MasterSyncPublicationStore.WriteAsync(publicationPath, publication, cancellationToken);
        Console.WriteLine($"MasterSync publication updated: {publicationPath}");
    }

    private static Uri BuildContentUri(string baseUrl, string relative, string sasToken)
    {
        var absolute = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), relative.TrimStart('/'));
        if (string.IsNullOrWhiteSpace(sasToken)) return absolute;
        var token = sasToken.TrimStart('?', '&');
        var separator = string.IsNullOrEmpty(absolute.Query) ? "?" : "&";
        return new Uri(absolute + separator + token);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<LocalMasterDataMetadata> CreateLocalMasterDataMetadataAsync(
        MasterDataManifest sourceManifest,
        string databasePath,
        CancellationToken cancellationToken)
    {
        var sha256 = await ComputeSha256Async(databasePath, cancellationToken);
        return new LocalMasterDataMetadata(
            sourceManifest.Version,
            sourceManifest.Uri,
            sourceManifest.PublishTimestamp,
            sha256);
    }

    private static async Task WriteMasterManifestAsync(
        string manifestPath,
        LocalMasterDataMetadata masterData,
        EnvironmentResult environment,
        string databasePath,
        CancellationToken cancellationToken)
    {
        var manifestOutput = new
        {
            masterData.Version,
            masterData.Uri,
            masterData.PublishTimestamp,
            environment.AssetVersion,
            environment.AssetUrl,
            environment.StaticContentUrl,
            environment.PhotoContentUrl,
            PublishedAt = DateTimeOffset.UtcNow,
            Size = new FileInfo(databasePath).Length,
            Sha256 = masterData.Sha256
        };
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(
                manifestOutput,
                new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private static async Task<string?> ReadLocalMasterVersionAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(path, ct));
            if (document.RootElement.TryGetProperty("SourceVersion", out var sourceVersion))
                return sourceVersion.ToString();
            return document.RootElement.TryGetProperty("Version", out var version)
                ? version.ToString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> ReadTextIfExistsAsync(string path, CancellationToken ct)
        => File.Exists(path) ? (await File.ReadAllTextAsync(path, ct)).Trim() : null;

    private sealed record LocalMasterDataMetadata(
        string Version,
        string Uri,
        long PublishTimestamp,
        string Sha256);
}
