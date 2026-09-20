using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sirius.Toolbox.IO;

namespace Sirius.Toolbox.Assets;

/// <summary>
/// CDN 资源镜像服务：刷新 Addressables catalog、notations、static-assets 与（可选）剧集场景，
/// 增量下载缺失对象并维护资源根目录下的 manifests/cdn_&lt;assetVersion&gt;.json。
/// CDN asset mirror: refreshes Addressables catalogs, notations, static assets and optionally
/// episode scenes, downloads missing objects incrementally and maintains
/// manifests/cdn_&lt;assetVersion&gt;.json under the selected asset root.
/// </summary>
public sealed class CdnAssetMirrorService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// 校验镜像选项。缺少必要地址时直接抛出，避免产生半成品目录。
    /// Validates mirror options and fails fast instead of producing a half-populated tree.
    /// </summary>
    public static void ValidateOptions(CdnAssetMirrorOptions options, CdnAssetMirrorContext context)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("资源镜像输出目录不能为空。", nameof(options));
        if (string.IsNullOrWhiteSpace(context.AssetBaseUrl))
            throw new InvalidDataException("官方环境没有返回 AssetUrl，无法镜像 CDN 资源。");
        if (string.IsNullOrWhiteSpace(context.AssetVersion))
            throw new InvalidDataException("官方环境没有返回 AssetVersion，无法镜像 CDN 资源。");
        if (!options.SkipStaticAssets && string.IsNullOrWhiteSpace(context.StaticContentBaseUrl))
            throw new InvalidDataException("官方环境没有返回 StaticContentUrl，无法镜像 static-assets；可启用 SkipStaticAssets 跳过。");
        if (!options.SkipScenes && string.IsNullOrWhiteSpace(context.MasterDataVersion))
            throw new InvalidDataException("缺少 MasterData 版本，无法解析剧集场景；可启用 SkipScenes 跳过。");
        if (options.Concurrency <= 0)
            throw new ArgumentException("并发数必须大于零。", nameof(options));
        if (options.Retries < 0)
            throw new ArgumentException("重试次数不能为负数。", nameof(options));
    }

    public async Task<CdnAssetMirrorResult> MirrorAsync(
        CdnAssetMirrorOptions options,
        CdnAssetMirrorContext context,
        IEpisodeDetailApi? episodeApi = null,
        IProgress<CdnAssetMirrorProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options, context);
        if (!options.SkipScenes && episodeApi is null)
            throw new ArgumentException("启用场景镜像时必须提供官方剧集详情接口。", nameof(episodeApi));

        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ServerCertificateCustomValidationCallback = options.InsecureTls
                ? HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                : null
        };
        using var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(Math.Max(1, options.TimeoutMinutes))
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, identity");

        var session = new Session(options, context, http, progress);
        return await session.RunAsync(episodeApi, cancellationToken);
    }

    private sealed class Session
    {
        private readonly CdnAssetMirrorOptions _options;
        private readonly CdnAssetMirrorContext _context;
        private readonly HttpClient _http;
        private readonly IProgress<CdnAssetMirrorProgress>? _progress;
        private readonly string _root;
        private readonly string _assetRoot;
        private readonly string _assetBase;
        private readonly string _staticContentBase;
        private readonly string _assetVersion;
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private CdnAssetManifest _manifest = new();
        private string _manifestPath = string.Empty;
        private int _episodeIndexFailureCount;
        private int _downloadedCount;
        private int _notFoundCount;
        private int _removedCount;

        public Session(
            CdnAssetMirrorOptions options,
            CdnAssetMirrorContext context,
            HttpClient http,
            IProgress<CdnAssetMirrorProgress>? progress)
        {
            _options = options;
            _context = context;
            _http = http;
            _progress = progress;
            _root = Path.GetFullPath(options.OutputDirectory);
            _assetRoot = options.UseOutputDirectoryAsAssetRoot
                ? _root
                : Path.Combine(_root, "assets");
            _assetBase = context.AssetBaseUrl.TrimEnd('/');
            _staticContentBase = context.StaticContentBaseUrl.TrimEnd('/');
            _assetVersion = context.AssetVersion;
        }

        public async Task<CdnAssetMirrorResult> RunAsync(IEpisodeDetailApi? episodeApi, CancellationToken ct)
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(_assetRoot);
            _manifestPath = Path.Combine(_assetRoot, "manifests", _options.ManifestFileName(_assetVersion));
            _manifest = await LoadManifestAsync(_manifestPath, ct);
            await ImportPreviousManifestRecordsAsync(ct);
            _manifest.AssetBase = _assetBase;
            _manifest.AssetVersion = _assetVersion;

            Report(CdnAssetMirrorStage.Catalog, "正在读取 Addressables catalog…");
            var catalogs = await ResolveCatalogsAsync(ct);

            Report(CdnAssetMirrorStage.Notations, "正在发现 notations…");
            ResolveNotations();

            if (_options.SkipStaticAssets)
            {
                Report(CdnAssetMirrorStage.StaticAssets, "StaticContentUrl 资源下载已禁用。");
            }
            else
            {
                Report(CdnAssetMirrorStage.StaticAssets, "正在发现 static-assets…");
                ResolveStaticAssets();
            }

            if (_options.SkipScenes)
            {
                Report(CdnAssetMirrorStage.Scenes, "剧集场景索引与下载已禁用。");
            }
            else
            {
                Report(CdnAssetMirrorStage.Scenes, "正在解析剧集场景索引…");
                await ResolveScenesAsync(episodeApi!, ct);
            }

            _manifest.Catalogs = catalogs;
            await SaveManifestAsync(ct);
            Report(CdnAssetMirrorStage.Manifest, $"清单已写入 {_manifestPath}");

            if (_options.CatalogOnly)
            {
                Report(CdnAssetMirrorStage.Complete, "仅更新 catalog 模式完成。");
                return BuildResult();
            }

            var records = _manifest.Objects.Values
                .Where(x => !_options.SkipScenes
                            || !string.Equals(x.Category, CdnAssetMirrorOptions.ScenesCategory, StringComparison.Ordinal))
                .Where(x => !_options.SkipStaticAssets
                            || !string.Equals(x.Category, CdnAssetMirrorOptions.StaticAssetsCategory, StringComparison.Ordinal))
                .Where(x => _options.ForceAssets
                            || (x.Status != CdnAssetStatus.Complete && x.Status != CdnAssetStatus.NotFound)
                            || (x.Status == CdnAssetStatus.Complete && !File.Exists(GetFinalPath(x))))
                .ToList();

            Report(
                CdnAssetMirrorStage.Download,
                $"正在下载 {records.Count} / {_manifest.Objects.Count} 个对象，并发 {Math.Max(1, _options.Concurrency)}…",
                0,
                records.Count);

            var failures = new ConcurrentBag<string>();
            var started = 0;
            var completed = 0;
            await Parallel.ForEachAsync(
                records,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _options.Concurrency), CancellationToken = ct },
                async (record, token) =>
                {
                    var itemNumber = Interlocked.Increment(ref started);
                    var itemLabel = $"{record.Category} {record.RelativePath}";
                    try
                    {
                        await DownloadOneAsync(record, token);
                        Interlocked.Increment(ref _downloadedCount);
                        Report(
                            CdnAssetMirrorStage.Download,
                            $"[OK {itemNumber}/{records.Count}] {itemLabel}",
                            Interlocked.Increment(ref completed),
                            records.Count);
                    }
                    catch (HttpRequestException ex) when (
                        ex.StatusCode == HttpStatusCode.NotFound
                        && (string.Equals(record.Category, CdnAssetMirrorOptions.NotationsCategory, StringComparison.Ordinal)
                            || string.Equals(record.Category, CdnAssetMirrorOptions.StaticAssetsCategory, StringComparison.Ordinal)))
                    {
                        record.Status = CdnAssetStatus.NotFound;
                        record.Error = ex.Message;
                        record.UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        Interlocked.Increment(ref _notFoundCount);
                        await SaveManifestAsync(token);
                        Report(
                            CdnAssetMirrorStage.Download,
                            $"[MISS {itemNumber}/{records.Count}] {itemLabel} 返回 404；可使用 ForceAssets 重试。",
                            Interlocked.Increment(ref completed),
                            records.Count);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        record.Status = CdnAssetStatus.Failed;
                        record.Error = ex.Message;
                        record.UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        failures.Add($"{record.Url}: {ex.Message}");
                        await SaveManifestAsync(token);
                        Report(
                            CdnAssetMirrorStage.Download,
                            $"[FAIL {itemNumber}/{records.Count}] {itemLabel} error={ex.Message}",
                            Interlocked.Increment(ref completed),
                            records.Count);
                    }
                });

            await SaveManifestAsync(ct);
            if (!failures.IsEmpty)
                throw new InvalidOperationException($"CDN 镜像完成，但有 {failures.Count} 个文件失败。重新运行可续传/重试。");
            if (_episodeIndexFailureCount > 0)
                throw new InvalidOperationException(
                    $"CDN 镜像已下载可用文件，但有 {_episodeIndexFailureCount} 个剧集场景索引失败。重新运行可重试。");

            Report(CdnAssetMirrorStage.Complete, "CDN 资源镜像完成。");
            return BuildResult();
        }

        private async Task<CdnAssetCatalogRecord> DiscoverCatalogAsync(
            string category,
            string platform,
            CancellationToken ct,
            RemoteCatalogHash? knownRemote = null)
        {
            var catalogDir = GetCatalogDirectory(category, platform);
            Directory.CreateDirectory(catalogDir);
            var errors = new List<string>();
            foreach (var url in CatalogCandidates(category, platform))
            {
                try
                {
                    using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        errors.Add($"{url}: {(int)response.StatusCode}");
                        continue;
                    }

                    var data = AssetCatalogParser.DecodeCatalogBytes(await response.Content.ReadAsByteArrayAsync(ct));
                    if (data.Length == 0 || data[0] != (byte)'{')
                    {
                        errors.Add($"{url}: not JSON");
                        continue;
                    }

                    var localJson = LocalCatalogJsonPath(category, platform);
                    AtomicFile.WriteAllBytes(localJson, data);
                    await WriteBrotliCatalogAsync(localJson + ".br", data, ct);

                    var hashUrl = AssetCatalogParser.GetCatalogHashUrl(url);
                    var localHash = string.Empty;
                    var hash = string.Empty;
                    try
                    {
                        byte[]? hashBytes = null;
                        if (knownRemote is not null)
                        {
                            hashBytes = knownRemote.Value.Bytes;
                        }
                        else
                        {
                            using var hashResponse = await _http.GetAsync(hashUrl, ct);
                            if (hashResponse.IsSuccessStatusCode)
                                hashBytes = await hashResponse.Content.ReadAsByteArrayAsync(ct);
                        }

                        if (hashBytes is { Length: > 0 })
                        {
                            localHash = LocalCatalogHashPath(category, platform);
                            AtomicFile.WriteAllBytes(localHash, hashBytes);
                            hash = Encoding.UTF8.GetString(hashBytes).Trim();
                        }
                    }
                    catch (Exception ex) when (ex is HttpRequestException or IOException)
                    {
                        // 哈希文件可选；catalog 本体已经落地。 The hash file is optional.
                    }

                    Report(CdnAssetMirrorStage.Catalog, $"{category} ({platform}): {url}");
                    return new CdnAssetCatalogRecord(category, url, hashUrl, localJson, localHash, hash, platform);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add($"{url}: {ex.Message}");
                }
            }

            throw new InvalidOperationException(
                $"无法获取 {category}（{platform}）的 catalog，已尝试：\n{string.Join('\n', errors)}");
        }

        private static async Task WriteBrotliCatalogAsync(string path, byte[] json, CancellationToken ct)
        {
            var partial = path + ".part";
            await using (var target = new FileStream(
                             partial, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            await using (var brotli = new System.IO.Compression.BrotliStream(
                             target, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: false))
            {
                await brotli.WriteAsync(json, ct);
            }

            File.Move(partial, path, true);
        }

        private async Task ImportPreviousManifestRecordsAsync(CancellationToken ct)
        {
            var directory = Path.GetDirectoryName(_manifestPath)!;
            if (!Directory.Exists(directory))
                return;

            foreach (var path in Directory.EnumerateFiles(directory, "cdn_*.json"))
            {
                if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(_manifestPath), StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var previous = await LoadManifestAsync(path, ct);
                    foreach (var pair in previous.Objects)
                    {
                        if (_manifest.Objects.ContainsKey(pair.Key))
                            continue;

                        var old = pair.Value;
                        if (old.Status != CdnAssetStatus.Complete || !File.Exists(GetFinalPath(old)))
                            continue;

                        _manifest.Objects[pair.Key] = new CdnAssetObjectRecord
                        {
                            Url = old.Url,
                            RelativePath = old.RelativePath,
                            Category = old.Category,
                            ExpectedSize = old.ExpectedSize,
                            DownloadedSize = old.DownloadedSize,
                            ETag = old.ETag,
                            LastModified = old.LastModified,
                            ContentMd5 = old.ContentMd5,
                            Sha256 = old.Sha256,
                            AliasOfUrl = old.AliasOfUrl,
                            Status = CdnAssetStatus.Complete,
                            Attempts = old.Attempts,
                            UpdatedAtUnixMs = old.UpdatedAtUnixMs
                        };
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Report(CdnAssetMirrorStage.Manifest, $"警告：无法导入旧的资源清单 {path}：{ex.Message}");
                }
            }
        }

        private void Upsert(CdnAssetObjectRecord incoming)
        {
            if (_manifest.Objects.TryGetValue(incoming.Url, out var old))
            {
                var pathChanged = !string.Equals(old.RelativePath, incoming.RelativePath, StringComparison.Ordinal);
                var wasAlias = !string.IsNullOrWhiteSpace(old.AliasOfUrl);
                old.Category = incoming.Category;
                old.RelativePath = incoming.RelativePath;
                old.AliasOfUrl = null;
                if (pathChanged || wasAlias)
                {
                    // 之前的 iOS 别名可能带有 Android 元数据并指向 Android 文件；
                    // 强制重新下载与校验 iOS 对象。
                    // A previous iOS alias may contain Android metadata; force a fresh download.
                    old.ExpectedSize = 0;
                    old.DownloadedSize = 0;
                    old.ETag = null;
                    old.LastModified = null;
                    old.ContentMd5 = null;
                    old.Sha256 = null;
                    old.Error = null;
                    old.Status = CdnAssetStatus.Pending;
                }

                return;
            }

            _manifest.Objects[incoming.Url] = incoming;
        }

        private string GetFinalPath(CdnAssetObjectRecord record) =>
            Path.Combine(_assetRoot, "files", record.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        private string GetCatalogDirectory(string category, string platform) =>
            string.Equals(platform, "Android", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(_assetRoot, "catalogs", category)
                : Path.Combine(_assetRoot, "catalogs", category, CdnAssetMirrorOptions.SanitizeFileName(platform));

        private string LocalCatalogJsonPath(string category, string platform) =>
            Path.Combine(
                GetCatalogDirectory(category, platform),
                $"catalog_{CdnAssetMirrorOptions.SanitizeFileName(_assetVersion)}.json");

        private string LocalCatalogHashPath(string category, string platform) =>
            Path.Combine(
                GetCatalogDirectory(category, platform),
                $"catalog_{CdnAssetMirrorOptions.SanitizeFileName(_assetVersion)}.hash");

        private CdnAssetCatalogRecord? GetCachedCatalog(string category, string platform)
        {
            var preferredJsonUrl = CatalogCandidates(category, platform).First();
            var recorded = _manifest.Catalogs.FirstOrDefault(x =>
                string.Equals(x.Category, category, StringComparison.Ordinal)
                && (string.Equals(x.Platform, platform, StringComparison.OrdinalIgnoreCase)
                    || (string.Equals(platform, "Android", StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrWhiteSpace(x.Platform)))
                && File.Exists(x.LocalJson));
            if (recorded is not null)
            {
                return recorded with
                {
                    JsonUrl = preferredJsonUrl,
                    HashUrl = AssetCatalogParser.GetCatalogHashUrl(preferredJsonUrl)
                };
            }

            var localJson = LocalCatalogJsonPath(category, platform);
            if (!File.Exists(localJson))
                return null;

            var jsonUrl = preferredJsonUrl;
            var hashUrl = AssetCatalogParser.GetCatalogHashUrl(jsonUrl);
            var localHash = LocalCatalogHashPath(category, platform);
            var hash = File.Exists(localHash) ? File.ReadAllText(localHash).Trim() : string.Empty;
            return new CdnAssetCatalogRecord(
                category, jsonUrl, hashUrl, localJson,
                File.Exists(localHash) ? localHash : string.Empty, hash, platform);
        }

        private async Task<CdnAssetCatalogRecord> ResolveCatalogAsync(string category, string platform, CancellationToken ct)
        {
            var cached = GetCachedCatalog(category, platform);
            if (cached is null)
                return await DiscoverCatalogAsync(category, platform, ct);

            if (_options.ForceAssets)
            {
                BackupCatalog(cached);
                return await DiscoverCatalogAsync(category, platform, ct);
            }

            var remote = await TryGetRemoteCatalogHashAsync(category, platform, ct);
            if (remote is null)
            {
                Report(CdnAssetMirrorStage.Catalog, $"警告：{category} 的远端 catalog 哈希不可用，改用本地缓存 {cached.LocalJson}");
                return cached;
            }

            var localHash = !string.IsNullOrWhiteSpace(cached.Hash)
                ? cached.Hash.Trim()
                : File.Exists(cached.LocalHash)
                    ? (await File.ReadAllTextAsync(cached.LocalHash, ct)).Trim()
                    : string.Empty;
            if (string.Equals(localHash, remote.Value.Hash, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(cached.LocalHash) || !File.Exists(cached.LocalHash))
                {
                    var localHashPath = LocalCatalogHashPath(category, platform);
                    AtomicFile.WriteAllBytes(localHashPath, remote.Value.Bytes);
                    cached = cached with { LocalHash = localHashPath };
                }

                Report(CdnAssetMirrorStage.Catalog, $"{category}: catalog 未变化，hash={remote.Value.Hash}");
                return cached with
                {
                    JsonUrl = remote.Value.JsonUrl,
                    HashUrl = remote.Value.HashUrl,
                    Hash = remote.Value.Hash
                };
            }

            Report(
                CdnAssetMirrorStage.Catalog,
                $"{category}: catalog 已变化 old={localHash} new={remote.Value.Hash}，正在刷新并比对对象…");
            BackupCatalog(cached);
            return await DiscoverCatalogAsync(category, platform, ct, remote);
        }

        private async Task<CdnAssetCatalogRecord?> ResolveIosCatalogAsync(
            string category,
            CdnAssetCatalogRecord androidCatalog,
            CancellationToken ct)
        {
            var remote = await TryGetRemoteCatalogHashAsync(category, "iOS", ct);
            if (remote is null)
            {
                Report(CdnAssetMirrorStage.Catalog, $"警告：{category} 的 iOS catalog 哈希不可用，继续使用 Android catalog。");
                return null;
            }

            if (!string.IsNullOrWhiteSpace(androidCatalog.Hash)
                && string.Equals(androidCatalog.Hash, remote.Value.Hash, StringComparison.OrdinalIgnoreCase))
            {
                return new CdnAssetCatalogRecord(
                    category,
                    remote.Value.JsonUrl,
                    remote.Value.HashUrl,
                    androidCatalog.LocalJson,
                    androidCatalog.LocalHash,
                    remote.Value.Hash,
                    "iOS");
            }

            var cached = GetCachedCatalog(category, "iOS");
            if (cached is not null
                && string.Equals(cached.Hash, remote.Value.Hash, StringComparison.OrdinalIgnoreCase))
            {
                return cached with
                {
                    JsonUrl = remote.Value.JsonUrl,
                    HashUrl = remote.Value.HashUrl,
                    Hash = remote.Value.Hash,
                    Platform = "iOS"
                };
            }

            if (cached is not null)
                BackupCatalog(cached);
            return await DiscoverCatalogAsync(category, "iOS", ct, remote);
        }

        private async Task<RemoteCatalogHash?> TryGetRemoteCatalogHashAsync(
            string category,
            string platform,
            CancellationToken ct)
        {
            foreach (var jsonUrl in CatalogCandidates(category, platform))
            {
                var hashUrl = AssetCatalogParser.GetCatalogHashUrl(jsonUrl);
                try
                {
                    using var response = await _http.GetAsync(hashUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!response.IsSuccessStatusCode)
                        continue;

                    var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                    var hash = Encoding.UTF8.GetString(bytes).Trim();
                    if (hash.Length == 0)
                        continue;

                    return new RemoteCatalogHash(jsonUrl, hashUrl, bytes, hash);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    // 继续尝试其它受支持的 catalog 地址形态。 Try the next supported catalog URL shape.
                }
            }

            return null;
        }

        private static void BackupCatalog(CdnAssetCatalogRecord catalog)
        {
            foreach (var path in new[] { catalog.LocalJson, catalog.LocalJson + ".br", catalog.LocalHash })
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Copy(path, path + ".bck", overwrite: true);
            }
        }

        private IEnumerable<string> CatalogCandidates(string category, string platform) =>
            AssetCatalogParser.CatalogCandidates(_assetBase, _assetVersion, category, platform, _options.CatalogTemplate);

        private async Task<List<CdnAssetCatalogRecord>> ResolveCatalogsAsync(CancellationToken ct)
        {
            var catalogs = new List<CdnAssetCatalogRecord>();
            foreach (var category in _options.ResolveCategories())
            {
                var androidCatalog = await ResolveCatalogAsync(category, "Android", ct);
                catalogs.Add(androidCatalog);
                var androidBytes = await File.ReadAllBytesAsync(androidCatalog.LocalJson, ct);
                var androidEntries = AssetCatalogParser.Parse(
                    androidBytes, androidCatalog.JsonUrl, category, "Android", _assetBase, _assetVersion);
                var entries = new List<CdnAssetObjectRecord>(androidEntries);

                var iosCatalog = await ResolveIosCatalogAsync(category, androidCatalog, ct);
                if (iosCatalog is not null)
                {
                    catalogs.Add(iosCatalog);
                    if (!string.IsNullOrWhiteSpace(androidCatalog.Hash)
                        && string.Equals(androidCatalog.Hash, iosCatalog.Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        // catalog 字节相同并不代表平台二进制相同：共享 catalog 文件，
                        // 但仍然从 iOS 资源池下载 iOS 对象。
                        // An identical catalog does not prove identical platform blobs.
                        var iosEntries = AssetCatalogParser.BuildPlatformEntries(androidEntries, "iOS");
                        entries.AddRange(iosEntries);
                        Report(
                            CdnAssetMirrorStage.Catalog,
                            $"{category}: iOS catalog 哈希与 Android 相同；将从 iOS 池下载 {iosEntries.Count} 个对象。");
                    }
                    else
                    {
                        var iosBytes = await File.ReadAllBytesAsync(iosCatalog.LocalJson, ct);
                        var iosEntries = AssetCatalogParser.Parse(
                            iosBytes, iosCatalog.JsonUrl, category, "iOS", _assetBase, _assetVersion);
                        entries.AddRange(iosEntries);
                        Report(
                            CdnAssetMirrorStage.Catalog,
                            $"{category}: iOS catalog 与 Android 有差异；发现 {iosEntries.Count} 个 iOS 远端对象。");
                    }
                }

                var currentUrls = entries.Select(x => x.Url).ToHashSet(StringComparer.Ordinal);
                var previousUrls = _manifest.Objects
                    .Where(x => string.Equals(x.Value.Category, category, StringComparison.Ordinal))
                    .Select(x => x.Key)
                    .ToHashSet(StringComparer.Ordinal);
                var staleUrls = previousUrls.Except(currentUrls, StringComparer.Ordinal).ToList();
                var added = currentUrls.Except(previousUrls, StringComparer.Ordinal).Count();
                var unchanged = currentUrls.Intersect(previousUrls, StringComparer.Ordinal).Count();
                Report(
                    CdnAssetMirrorStage.Catalog,
                    $"{category}: 增量差异 新增={added} 未变={unchanged} 移除={staleUrls.Count} 合计={entries.Count}");

                foreach (var staleUrl in staleUrls)
                {
                    if (_manifest.Objects.Remove(staleUrl))
                        _removedCount++;
                }

                foreach (var entry in entries)
                    Upsert(entry);
            }

            return catalogs;
        }

        private void ResolveNotations()
        {
            var entries = NotationAssetDiscovery.Discover(new NotationDiscoveryOptions
            {
                RootDirectory = _root,
                AssetBaseUrl = _assetBase,
                Log = message => Report(CdnAssetMirrorStage.Notations, message)
            });
            ReplaceSupplementalCategory(CdnAssetMirrorOptions.NotationsCategory, entries);
        }

        private void ResolveStaticAssets()
        {
            var entries = StaticAssetDiscovery.Discover(new StaticAssetDiscoveryOptions
            {
                RootDirectory = _root,
                AssetDirectory = _assetRoot,
                StaticContentBaseUrl = _staticContentBase,
                Log = message => Report(CdnAssetMirrorStage.StaticAssets, message)
            });
            ReplaceSupplementalCategory(CdnAssetMirrorOptions.StaticAssetsCategory, entries, removeWhenEmpty: true);
        }

        private async Task ResolveScenesAsync(IEpisodeDetailApi api, CancellationToken ct)
        {
            var result = await new EpisodeSceneDiscoveryService().DiscoverAsync(
                new EpisodeSceneDiscoveryOptions
                {
                    RootDirectory = _root,
                    AssetDirectory = _assetRoot,
                    MasterDataVersion = _context.MasterDataVersion,
                    MasterDataBaseUrl = _context.MasterDataBaseUrl,
                    Concurrency = _options.Concurrency,
                    Retries = _options.Retries,
                    Log = message => Report(CdnAssetMirrorStage.Scenes, message)
                },
                api,
                ShouldRefreshSceneSource,
                ct);

            _episodeIndexFailureCount = result.FailureCount;
            Report(
                CdnAssetMirrorStage.Scenes,
                $"场景索引：剧集={result.EpisodeCount} 已解析={result.ResolvedCount} 失败={result.FailureCount} 对象={result.Objects.Count}");
            ReplaceSupplementalCategory(CdnAssetMirrorOptions.ScenesCategory, result.Objects);
        }

        private bool ShouldRefreshSceneSource(string source)
        {
            if (!EpisodeSceneDiscoveryService.TryResolveSceneSource(source, _context.MasterDataBaseUrl, out var uri))
                return false;

            return _manifest.Objects.TryGetValue(uri.AbsoluteUri, out var record)
                   && record.Status == CdnAssetStatus.Failed
                   && record.Error is { } error
                   && (error.Contains("403", StringComparison.Ordinal)
                       || error.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)
                       || error.Contains("AuthenticationFailed", StringComparison.OrdinalIgnoreCase));
        }

        private void ReplaceSupplementalCategory(
            string category,
            IReadOnlyCollection<CdnAssetObjectRecord> entries,
            bool removeWhenEmpty = false)
        {
            if (entries.Count == 0 && !removeWhenEmpty)
                return;

            Report(ResolveStage(category), $"{category}: 发现 {entries.Count} 个文件");
            var currentUrls = entries.Select(x => x.Url).ToHashSet(StringComparer.Ordinal);
            var staleUrls = _manifest.Objects
                .Where(x => string.Equals(x.Value.Category, category, StringComparison.Ordinal)
                            && !currentUrls.Contains(x.Key))
                .Select(x => x.Key)
                .ToList();
            foreach (var staleUrl in staleUrls)
            {
                if (_manifest.Objects.Remove(staleUrl))
                    _removedCount++;
            }

            foreach (var entry in entries)
                Upsert(entry);
        }

        private static CdnAssetMirrorStage ResolveStage(string category) => category switch
        {
            CdnAssetMirrorOptions.NotationsCategory => CdnAssetMirrorStage.Notations,
            CdnAssetMirrorOptions.StaticAssetsCategory => CdnAssetMirrorStage.StaticAssets,
            CdnAssetMirrorOptions.ScenesCategory => CdnAssetMirrorStage.Scenes,
            _ => CdnAssetMirrorStage.Manifest
        };

        private async Task DownloadOneAsync(CdnAssetObjectRecord record, CancellationToken ct)
        {
            var finalPath = GetFinalPath(record);
            var partPath = finalPath + ".part";
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

            if (!_options.ForceAssets && File.Exists(finalPath))
            {
                var existing = new FileInfo(finalPath);
                if (record.ExpectedSize <= 0 || existing.Length == record.ExpectedSize)
                {
                    if (!string.IsNullOrWhiteSpace(record.Sha256))
                    {
                        var hash = await HashFileAsync(finalPath, ct);
                        if (hash.Equals(record.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            record.Status = CdnAssetStatus.Complete;
                            record.DownloadedSize = existing.Length;
                            return;
                        }
                    }
                    else
                    {
                        record.Sha256 = await HashFileAsync(finalPath, ct);
                        record.Status = CdnAssetStatus.Complete;
                        record.DownloadedSize = existing.Length;
                        return;
                    }
                }
            }

            Exception? last = null;
            var maxAttempts = Math.Max(1, _options.Retries + 1);
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                record.Attempts++;
                record.Status = CdnAssetStatus.Downloading;
                record.Error = null;
                await SaveManifestAsync(ct);
                try
                {
                    // 剧集场景 blob 可能带 HTTP content encoding；本地 .part 保存的是解码后
                    // 的字节，长度不能作为编码后 blob 的 Range 偏移，因此场景一律从 0 开始。
                    // Episode scene blobs may be served with content encoding, so the local
                    // .part length is not a valid Range offset into the encoded blob.
                    var allowResume = !string.Equals(record.Category, CdnAssetMirrorOptions.ScenesCategory, StringComparison.Ordinal);
                    if (!allowResume && File.Exists(partPath))
                        File.Delete(partPath);

                    var offset = allowResume && File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
                    using var request = new HttpRequestMessage(HttpMethod.Get, record.Url);
                    if (offset > 0)
                        request.Headers.Range = new RangeHeaderValue(offset, null);

                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (offset > 0 && response.StatusCode == HttpStatusCode.OK)
                    {
                        File.Delete(partPath);
                        offset = 0;
                    }

                    if (offset > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        var total = response.Content.Headers.ContentRange?.Length;
                        if (total == offset)
                        {
                            File.Move(partPath, finalPath, true);
                            await FinalizeRecordAsync(record, finalPath, response, ct);
                            return;
                        }

                        File.Delete(partPath);
                        throw new IOException("远端对象拒绝续传区间，已重置分片文件。");
                    }

                    response.EnsureSuccessStatusCode();
                    // AutomaticDecompression 会返回解码后字节，而 Content-Length 描述的是
                    // 编码后的线格式长度；只有没有 Content-Encoding 时才校验字节数。
                    var skipLengthValidation = !allowResume || response.Content.Headers.ContentEncoding.Count > 0;
                    var expectedTotal = skipLengthValidation
                        ? 0
                        : response.Content.Headers.ContentRange?.Length
                          ?? (response.Content.Headers.ContentLength is { } contentLength ? contentLength + offset : 0);

                    // Windows 上必须先关闭流再 File.Move 重命名分片文件。
                    // Both streams must be closed before moving the .part file on Windows.
                    await using (var source = await response.Content.ReadAsStreamAsync(ct))
                    await using (var target = new FileStream(
                                     partPath,
                                     offset > 0 ? FileMode.Append : FileMode.Create,
                                     FileAccess.Write,
                                     FileShare.Read,
                                     1024 * 1024,
                                     FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await source.CopyToAsync(target, 1024 * 1024, ct);
                        await target.FlushAsync(ct);
                    }

                    if (expectedTotal > 0 && new FileInfo(partPath).Length != expectedTotal)
                    {
                        throw new IOException(
                            $"下载后大小不匹配：本地={new FileInfo(partPath).Length} 期望={expectedTotal}");
                    }

                    File.Move(partPath, finalPath, true);
                    await FinalizeRecordAsync(record, finalPath, response, ct);
                    return;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    throw;
                }
                catch (Exception ex) when (attempt < maxAttempts && ex is not OperationCanceledException)
                {
                    last = ex;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt))), ct);
                }
            }

            throw last ?? new IOException("下载失败。");
        }

        private async Task FinalizeRecordAsync(
            CdnAssetObjectRecord record,
            string path,
            HttpResponseMessage response,
            CancellationToken ct)
        {
            var file = new FileInfo(path);
            var sha = await HashFileAsync(path, ct);
            if (!string.IsNullOrWhiteSpace(record.Sha256)
                && !sha.Equals(record.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("既有清单对象的 SHA-256 校验失败。");
            }

            record.ExpectedSize = response.Content.Headers.ContentRange?.Length ?? file.Length;
            record.DownloadedSize = file.Length;
            record.Sha256 = sha;
            record.ETag = response.Headers.ETag?.ToString();
            record.LastModified = response.Content.Headers.LastModified?.ToString("O");
            record.ContentMd5 = response.Content.Headers.ContentMD5 is { Length: > 0 } contentMd5
                ? Convert.ToBase64String(contentMd5)
                : null;
            record.Status = CdnAssetStatus.Complete;
            record.Error = null;
            record.UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await SaveManifestAsync(ct);
        }

        private async Task<CdnAssetManifest> LoadManifestAsync(string path, CancellationToken ct)
        {
            if (!File.Exists(path))
                return new CdnAssetManifest();
            try
            {
                await using var stream = File.OpenRead(path);
                return await JsonSerializer.DeserializeAsync<CdnAssetManifest>(stream, JsonOptions, ct)
                       ?? new CdnAssetManifest();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Report(CdnAssetMirrorStage.Manifest, $"警告：资源清单 {path} 无法读取，将重新建立：{ex.Message}");
                return new CdnAssetManifest();
            }
        }

        private async Task SaveManifestAsync(CancellationToken ct)
        {
            await _saveLock.WaitAsync(ct);
            string? temp = null;
            try
            {
                _manifest.UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Directory.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);
                // 固定的 .tmp 名在多个下载进程共用同一输出目录时会互相冲突，
                // 因此每个写入者使用私有临时文件。
                // A fixed .tmp name collides when two downloaders share an output directory.
                temp = $"{_manifestPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
                await using (var stream = File.Create(temp))
                {
                    await JsonSerializer.SerializeAsync(stream, _manifest, JsonOptions, ct);
                    await stream.FlushAsync(ct);
                }

                const int maxReplaceAttempts = 8;
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(_manifestPath)
                            && (File.GetAttributes(_manifestPath) & FileAttributes.ReadOnly) != 0)
                        {
                            File.SetAttributes(
                                _manifestPath,
                                File.GetAttributes(_manifestPath) & ~FileAttributes.ReadOnly);
                        }

                        File.Move(temp!, _manifestPath, true);
                        temp = null;
                        break;
                    }
                    catch (Exception ex) when (
                        (ex is IOException || ex is UnauthorizedAccessException)
                        && attempt < maxReplaceAttempts)
                    {
                        // 索引器、杀毒软件或其它下载进程可能短暂占用目标文件；
                        // 重试可以保住已下载文件而不是让整次镜像失败。
                        // Indexers, antivirus scanners and other downloader processes can
                        // briefly hold the destination on Windows.
                        await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(1000, 50 * (1 << (attempt - 1)))), ct);
                    }
                }
            }
            finally
            {
                if (temp is not null)
                {
                    try
                    {
                        File.Delete(temp);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }
                }

                _saveLock.Release();
            }
        }

        private static async Task<string> HashFileAsync(string path, CancellationToken ct)
        {
            await using var stream = File.OpenRead(path);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        }

        private void Report(CdnAssetMirrorStage stage, string message, int completed = 0, int total = 0) =>
            _progress?.Report(new CdnAssetMirrorProgress(stage, message, completed, total));

        private CdnAssetMirrorResult BuildResult() => new(
            _manifestPath,
            _assetVersion,
            _manifest.Objects.Count,
            _downloadedCount,
            _notFoundCount,
            _removedCount,
            _manifest.Catalogs.Count,
            _options.CatalogOnly);
    }
}
