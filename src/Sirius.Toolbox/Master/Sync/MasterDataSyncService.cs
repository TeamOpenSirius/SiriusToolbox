using System.Text.Json;
using Sirius.MasterData;
using Sirius.Toolbox.Assets;
using Sirius.Toolbox.IO;
using Sirius.Toolbox.Master.Networking;
using Sirius.Toolbox.Master.Persistence;
using Sirius.Toolbox.Master.Protocol;

namespace Sirius.Toolbox.Master.Sync;

/// <summary>
/// 官方 MasterData / CDN 同步选项。字段与旧 Sirius.AssetTool 的下载器对齐，去掉已废弃的
/// PostgreSQL 导入与 MasterIndex 构建开关。
/// Official MasterData / CDN sync options. Aligned with the retired Sirius.AssetTool downloader,
/// without the removed PostgreSQL import and MasterIndex build switches.
/// </summary>
public sealed record MasterDataSyncOptions(string OutputDirectory)
{
    /// <summary>master/json/.complete 标记格式；换格式会让既有导出失效并触发重建。</summary>
    public const string MasterJsonExportFormat = "object-schema-v1";

    public string ApiBootstrapUrl { get; init; } = "https://api.wds-stellarium.com";
    public string ApplicationVersion { get; init; } = "2.30.1";
    public string AuthenticationVersionSuffix { get; init; } = ".486";
    public int GameVersion { get; init; } = 2;
    public string RegistrationName { get; init; } = "ToolboxUser";
    public string Platform { get; init; } = "google-play";
    public string Fm { get; init; } = "0";
    public string? LoginToken { get; init; }
    public string? AccessToken { get; init; }
    public bool Force { get; init; }
    public bool ExportJson { get; init; } = true;
    public bool InsecureTls { get; init; }

    /// <summary>跳过 MasterMemory 数据库下载与导出。</summary>
    public bool SkipMasterData { get; init; }

    /// <summary>跳过全部 CDN 资源镜像。</summary>
    public bool SkipAssets { get; init; }

    /// <summary>跳过 static-assets（StaticContentUrl）发现与下载。</summary>
    public bool SkipStaticAssets { get; init; }

    /// <summary>
    /// 跳过剧集场景。场景需要对每个剧集调用一次官方 API，默认关闭。
    /// Skip episode scenes. Scenes need one authenticated API call per episode, so they are off by default.
    /// </summary>
    public bool SkipScenes { get; init; } = true;

    /// <summary>只刷新 catalog 与清单，不下载对象。</summary>
    public bool CatalogOnly { get; init; }

    /// <summary>忽略本地文件与状态，重新下载全部资源对象。</summary>
    public bool ForceAssets { get; init; }

    public IReadOnlyList<string> AssetCategories { get; init; } = [];
    public string? CatalogTemplate { get; init; }
    public int AssetConcurrency { get; init; } = 12;
    public int AssetRetries { get; init; } = 5;
    public int AssetTimeoutMinutes { get; init; } = 10;
    public string AssetUserAgent { get; init; } = "BestHTTP/2 v2.8.5";

    public string AuthenticationApplicationVersion => ApplicationVersion + AuthenticationVersionSuffix;
}

public sealed record MasterDataSyncProgress(string Stage, string Message);

public sealed record MasterDataSyncResult(
    string DatabasePath,
    string ManifestPath,
    string PublicationPath,
    string Version,
    long PublishTimestamp,
    bool Downloaded,
    bool ExportedJson,
    CdnAssetMirrorResult? Assets,
    MasterDatabaseVerification? Verification);

/// <summary>
/// 官方同步服务：注册/认证、下载并校验 MasterMemory 数据库、导出类型化 JSON、
/// 镜像 CDN 资源，并写出 state.json 与 publication.json。
/// Official sync service: registers/authenticates, downloads and verifies the MasterMemory
/// database, exports typed JSON, mirrors CDN assets and writes state.json plus publication.json.
/// </summary>
public sealed class MasterDataSyncService
{
    public static void ValidateOptions(MasterDataSyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("MasterData 输出目录不能为空。", nameof(options));
        if (!Uri.TryCreate(options.ApiBootstrapUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("官方 API 地址必须是 HTTP(S) 绝对地址。", nameof(options));
        if (string.IsNullOrWhiteSpace(options.ApplicationVersion))
            throw new ArgumentException("客户端版本不能为空。", nameof(options));
        if (options.GameVersion <= 0)
            throw new ArgumentException("GameVersion 必须大于零。", nameof(options));
        if (options.SkipMasterData && options.SkipAssets)
            throw new ArgumentException("MasterData 与 CDN 资源不能同时跳过。", nameof(options));
        if (options.AssetConcurrency <= 0)
            throw new ArgumentException("资源并发数必须大于零。", nameof(options));
        if (options.AssetRetries < 0)
            throw new ArgumentException("资源重试次数不能为负数。", nameof(options));
    }

    public async Task<MasterDataSyncResult> SyncAsync(
        MasterDataSyncOptions options,
        IProgress<MasterDataSyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        var root = Path.GetFullPath(options.OutputDirectory);
        var masterDirectory = Path.Combine(root, "master");
        var jsonDirectory = Path.Combine(masterDirectory, "json");
        var databasePath = Path.Combine(masterDirectory, "mastermemory.db");
        var manifestPath = Path.Combine(masterDirectory, "manifest.json");
        var statePath = Path.Combine(root, "state.json");
        var publicationPath = Path.Combine(root, "publication.json");
        Directory.CreateDirectory(masterDirectory);

        var state = await StateStore.LoadAsync(statePath, cancellationToken);
        var clientOptions = new MasterDownloadOptions
        {
            OutputDirectory = root,
            ApiBootstrapUrl = options.ApiBootstrapUrl,
            ApplicationVersion = options.ApplicationVersion,
            AuthenticationVersionSuffix = options.AuthenticationVersionSuffix,
            GameVersion = options.GameVersion,
            RegistrationName = options.RegistrationName,
            Platform = options.Platform,
            Fm = options.Fm,
            LoginToken = options.LoginToken,
            AccessToken = options.AccessToken,
            Force = options.Force,
            ExportJson = options.ExportJson,
            InsecureTls = options.InsecureTls
        };

        using var api = new SiriusApiClient(clientOptions);

        progress?.Report(new("环境", "正在读取官方环境配置…"));
        var environment = await api.GetEnvironmentAsync(cancellationToken);
        var apiBase = string.IsNullOrWhiteSpace(environment.ApiEndpoint)
            ? options.ApiBootstrapUrl
            : environment.ApiEndpoint;
        api.SetApiBase(apiBase);
        api.SetAssetVersion(environment.AssetVersion);

        // 缓存的访问令牌只在与当前客户端参数一致时才可复用，否则官方会以 440 拒绝。
        // A cached access token is only reusable when it was created for the current client parameters.
        var cachedTokenMatchesClient = state.GameVersion == options.GameVersion
            && string.Equals(
                state.AuthenticationApplicationVersion,
                clientOptions.AuthenticationApplicationVersion,
                StringComparison.Ordinal);

        state.ApiEndpoint = apiBase;
        state.MasterDataUrl = environment.MasterDataUrl;
        state.AssetUrl = environment.AssetUrl;
        state.StaticContentUrl = environment.StaticContentUrl;
        state.PhotoContentUrl = environment.PhotoContentUrl;
        state.AssetVersion = environment.AssetVersion;
        state.ApplicationVersion = environment.ApplicationVersion;
        state.AuthenticationApplicationVersion = clientOptions.AuthenticationApplicationVersion;
        state.GameVersion = options.GameVersion;
        await SaveStateAsync(statePath, state, cancellationToken);

        var accessToken = options.AccessToken ?? (cachedTokenMatchesClient ? state.AccessToken : null);
        var loginToken = options.LoginToken ?? state.LoginToken;
        if (options.AccessToken is null && !cachedTokenMatchesClient && !string.IsNullOrWhiteSpace(state.AccessToken))
            progress?.Report(new("认证", "缓存的访问令牌对应其它客户端参数，将重新认证。"));

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            if (string.IsNullOrWhiteSpace(loginToken))
            {
                progress?.Report(new("注册", $"没有登录令牌，正在注册工具账户 {options.RegistrationName}…"));
                var registration = await api.RegisterAsync(apiBase, options.RegistrationName, cancellationToken);
                loginToken = registration.Token;
                if (string.IsNullOrWhiteSpace(loginToken))
                    throw new InvalidDataException($"官方注册未返回登录令牌；error={registration.ErrorType}。");
                state.LoginToken = loginToken;
                await SaveStateAsync(statePath, state, cancellationToken);
            }

            progress?.Report(new("认证", "正在认证官方账户…"));
            var authentication = await api.AuthenticateAsync(apiBase, loginToken, cancellationToken);
            accessToken = authentication.Token;
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new InvalidDataException("官方认证未返回访问令牌。");
            state.LoginToken = loginToken;
            state.AccessToken = accessToken;
            await SaveStateAsync(statePath, state, cancellationToken);
        }

        api.SetBearerToken(accessToken);
        progress?.Report(new("登录", "正在登录官方服务…"));
        try
        {
            await api.LoginAsync(apiBase, cancellationToken);
        }
        catch (HttpRequestException ex) when (
            (int?)ex.StatusCode == 440 && !string.IsNullOrWhiteSpace(loginToken))
        {
            // 长生命周期的登录令牌仍然有效时，官方访问/会话令牌会过期。
            // 重新认证一次、持久化新令牌，然后重试登录。
            // Official access/session tokens expire while the long-lived login token stays valid.
            progress?.Report(new("认证", "缓存的访问令牌已过期（HTTP 440），正在重新认证…"));
            api.SetBearerToken(null);
            state.AccessToken = null;

            var authentication = await api.AuthenticateAsync(apiBase, loginToken, cancellationToken);
            accessToken = authentication.Token;
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new InvalidDataException("重新认证未返回访问令牌。");

            api.SetBearerToken(accessToken);
            state.AccessToken = accessToken;
            state.LoginToken = loginToken;
            await SaveStateAsync(statePath, state, cancellationToken);

            progress?.Report(new("登录", "重新认证成功，正在重试登录…"));
            await api.LoginAsync(apiBase, cancellationToken);
        }

        MasterDataSyncResult result;
        MasterDataManifest? currentMasterManifest = null;
        LocalMasterDataMetadata? currentMasterData = null;
        MasterDatabaseVerification? verification = null;
        var downloaded = false;
        var exportedJson = false;

        if (options.SkipMasterData)
        {
            progress?.Report(new("主数据", "已跳过 MasterData 下载。"));
        }
        else
        {
            progress?.Report(new("清单", "正在读取 MasterData 清单…"));
            var manifest = await api.GetMasterManifestAsync(apiBase, cancellationToken);
            currentMasterManifest = manifest;
            api.SetMasterDataVersion(manifest.Version);

            state.LoginToken = loginToken;
            state.AccessToken = accessToken;
            await SaveStateAsync(statePath, state, cancellationToken);

            var localMasterVersion = state.MasterDataVersion
                ?? await ReadLocalMasterVersionAsync(manifestPath, cancellationToken);
            var masterCurrent = !options.Force
                && string.Equals(localMasterVersion, manifest.Version, StringComparison.Ordinal)
                && File.Exists(databasePath);

            if (masterCurrent)
            {
                progress?.Report(new("下载", $"MasterData {manifest.Version} 已存在，跳过下载。"));
            }
            else
            {
                var masterUri = BuildContentUri(environment.MasterDataUrl, manifest.Uri, manifest.SasToken);
                progress?.Report(new("下载", $"正在下载 {masterUri}…"));
                if (File.Exists(databasePath))
                    File.Copy(databasePath, databasePath + ".bck", overwrite: true);
                await api.DownloadFileAsync(masterUri, databasePath, cancellationToken);
                downloaded = true;
            }

            progress?.Report(new("校验", "正在通过 MasterMemory 模型校验数据库…"));
            verification = MasterMemoryDatabaseService.Verify(databasePath);
            currentMasterData = new LocalMasterDataMetadata(
                manifest.Version,
                manifest.Uri,
                manifest.PublishTimestamp,
                verification.Sha256);
            await WriteMasterManifestAsync(
                manifestPath, currentMasterData, environment, databasePath, cancellationToken);

            state.MasterDataVersion = manifest.Version;
            await SaveStateAsync(statePath, state, cancellationToken);

            if (options.ExportJson)
            {
                // 导出标记包含格式与版本；只有完全匹配时才跳过解析。
                // The export marker encodes format and version; only an exact match skips parsing.
                var exportMarkerPath = Path.Combine(jsonDirectory, ".complete");
                var expectedMarker = $"{MasterDataSyncOptions.MasterJsonExportFormat}:{currentMasterData.Version}";
                var exportCurrent = !options.Force
                    && string.Equals(
                        await ReadTextIfExistsAsync(exportMarkerPath, cancellationToken),
                        expectedMarker,
                        StringComparison.Ordinal);
                if (exportCurrent)
                {
                    progress?.Report(new("导出", $"MasterData JSON {manifest.Version} 已导出，跳过解析。"));
                }
                else
                {
                    progress?.Report(new("导出", "正在导出类型化 MasterData JSON…"));
                    await new MasterMemoryExporter(message => progress?.Report(new("导出", message)))
                        .ExportAsync(databasePath, jsonDirectory, cancellationToken);
                    Directory.CreateDirectory(jsonDirectory);
                    await File.WriteAllTextAsync(exportMarkerPath, expectedMarker, cancellationToken);
                    exportedJson = true;
                }

                state.MasterJsonVersion = currentMasterData.Version;
                await SaveStateAsync(statePath, state, cancellationToken);
            }

            await WritePublicationAsync(
                publicationPath, root, masterDirectory, environment, currentMasterData, cancellationToken);
            progress?.Report(new("发布", $"publication.json 已更新：{publicationPath}"));
        }

        CdnAssetMirrorResult? assetResult = null;
        if (options.SkipAssets)
        {
            progress?.Report(new("资源", "已跳过 CDN 资源镜像。"));
        }
        else
        {
            var context = new CdnAssetMirrorContext
            {
                AssetBaseUrl = environment.AssetUrl,
                AssetVersion = environment.AssetVersion,
                StaticContentBaseUrl = environment.StaticContentUrl,
                MasterDataBaseUrl = environment.MasterDataUrl,
                MasterDataVersion = state.MasterDataVersion ?? string.Empty
            };
            var mirrorOptions = new CdnAssetMirrorOptions(root)
            {
                UseOutputDirectoryAsAssetRoot = true,
                AssetCategories = options.AssetCategories,
                CatalogTemplate = options.CatalogTemplate,
                CatalogOnly = options.CatalogOnly,
                ForceAssets = options.ForceAssets,
                Concurrency = options.AssetConcurrency,
                Retries = options.AssetRetries,
                TimeoutMinutes = options.AssetTimeoutMinutes,
                UserAgent = options.AssetUserAgent,
                SkipStaticAssets = options.SkipStaticAssets,
                SkipScenes = options.SkipScenes,
                InsecureTls = options.InsecureTls
            };
            CdnAssetMirrorService.ValidateOptions(mirrorOptions, context);
            api.SetMasterDataVersion(state.MasterDataVersion);

            progress?.Report(new("资源", "正在同步 CDN 资源（增量）…"));
            assetResult = await new CdnAssetMirrorService().MirrorAsync(
                mirrorOptions,
                context,
                api,
                new Progress<CdnAssetMirrorProgress>(
                    item => progress?.Report(new($"资源/{item.Stage}", item.Message))),
                cancellationToken);

            if (currentMasterData is not null)
            {
                await WritePublicationAsync(
                    publicationPath, root, masterDirectory, environment, currentMasterData, cancellationToken);
                progress?.Report(new("发布", $"publication.json 已更新（含 CDN 清单）：{publicationPath}"));
            }
        }

        state.LoginToken = loginToken;
        state.AccessToken = accessToken;
        await SaveStateAsync(statePath, state, cancellationToken);

        var version = currentMasterData?.Version ?? state.MasterDataVersion ?? string.Empty;
        result = new MasterDataSyncResult(
            databasePath,
            manifestPath,
            publicationPath,
            version,
            currentMasterManifest?.PublishTimestamp ?? 0,
            downloaded,
            exportedJson,
            assetResult,
            verification);

        progress?.Report(new("完成", $"官方同步完成：MasterData {version}。"));
        return result;
    }

    private sealed record LocalMasterDataMetadata(
        string Version,
        string Uri,
        long PublishTimestamp,
        string Sha256);

    private static async Task<string?> ReadLocalMasterVersionAsync(string manifestPath, CancellationToken ct)
    {
        if (!File.Exists(manifestPath))
            return null;
        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(manifestPath, ct));
            return document.RootElement.TryGetProperty("Version", out var version)
                   && version.ValueKind == JsonValueKind.String
                ? version.GetString()
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<string?> ReadTextIfExistsAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return (await File.ReadAllTextAsync(path, ct)).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task WriteMasterManifestAsync(
        string path,
        LocalMasterDataMetadata metadata,
        EnvironmentResult environment,
        string databasePath,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            metadata.Version,
            metadata.Uri,
            metadata.PublishTimestamp,
            environment.AssetVersion,
            environment.AssetUrl,
            environment.StaticContentUrl,
            Size = new FileInfo(databasePath).Length,
            metadata.Sha256,
            PublishedAt = DateTimeOffset.UtcNow
        }, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, payload + Environment.NewLine, ct);
    }

    /// <summary>
    /// 写出 publication.json。MasterIndexDatabase 保持为空：MasterIndexer 子项目已废弃，
    /// 服务端只通过 MasterMemory 读取 mastermemory.db。
    /// Writes publication.json. MasterIndexDatabase stays empty because the MasterIndexer
    /// subproject is retired and servers only read mastermemory.db through MasterMemory.
    /// </summary>
    private static async Task WritePublicationAsync(
        string publicationPath,
        string root,
        string masterDirectory,
        EnvironmentResult environment,
        LocalMasterDataMetadata masterData,
        CancellationToken ct)
    {
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
            CdnManifest = FindLatestCdnManifest(root),
            PublishedAt = DateTimeOffset.UtcNow
        };
        await MasterSyncPublicationStore.WriteAsync(publicationPath, publication, ct);
    }

    private static string? FindLatestCdnManifest(string root)
    {
        var manifestDirectory = Path.Combine(root, "manifests");
        if (!Directory.Exists(manifestDirectory))
            return null;

        return Directory.EnumerateFiles(manifestDirectory, "cdn_*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static Uri BuildContentUri(string baseUrl, string relative, string sasToken)
    {
        var absolute = new Uri(
            new Uri(Require(baseUrl, "官方环境没有返回 MasterData 地址。").TrimEnd('/') + "/"),
            Require(relative, "MasterData 清单没有返回文件路径。").TrimStart('/'));
        if (string.IsNullOrWhiteSpace(sasToken))
            return absolute;
        var separator = string.IsNullOrEmpty(absolute.Query) ? "?" : "&";
        return new Uri(absolute + separator + sasToken.TrimStart('?', '&'));
    }

    private static string Require(string? value, string message) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException(message);

    private static async Task SaveStateAsync(string statePath, DownloadState state, CancellationToken ct)
    {
        state.UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await StateStore.SaveAsync(statePath, state, ct);
    }
}
