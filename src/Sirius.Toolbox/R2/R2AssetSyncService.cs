using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sirius.Toolbox.R2;

public sealed record R2SyncOptions(string RootDirectory)
{
    public string Endpoint { get; init; } = MasterDataR2UploadDefaults.Endpoint;
    public string Bucket { get; init; } = MasterDataR2UploadDefaults.Bucket;
    public string? AccessKeyId { get; init; }
    public string? SecretAccessKey { get; init; }
    public string? SessionToken { get; init; }
    public string KeyPrefix { get; init; } = string.Empty;
    public int Concurrency { get; init; } = 16;
    public int MaxRetries { get; init; } = MasterDataR2UploadDefaults.MaxRetries;
    public bool Force { get; init; }
    public bool OnlyUploadChanged { get; init; }
    public bool DryRun { get; init; }
    public IReadOnlyList<R2CustomMapping> CustomMappings { get; init; } = Array.Empty<R2CustomMapping>();
}

public sealed record R2CustomMapping(string LocalDirectory, string ObjectPrefix)
{
    public static R2CustomMapping Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("自定义目录映射不能为空。", nameof(value));

        var separator = value.IndexOf('=');
        if (separator <= 0 || separator == value.Length - 1)
            throw new ArgumentException("自定义目录映射格式应为“本地目录=R2对象前缀”。", nameof(value));

        var localDirectory = value[..separator].Trim();
        var objectPrefix = value[(separator + 1)..].Trim();
        if (localDirectory.Length == 0 || objectPrefix.Length == 0)
            throw new ArgumentException("自定义目录映射的本地目录和 R2 对象前缀不能为空。", nameof(value));
        return new R2CustomMapping(localDirectory, objectPrefix);
    }

    public static IReadOnlyList<R2CustomMapping> ParseLines(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<R2CustomMapping>();

        return value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Parse)
            .ToArray();
    }
}

public sealed record R2SyncObjectPlan(
    string LocalPath,
    string ObjectKey,
    long Length,
    string ContentType,
    string? ContentEncoding);

public sealed record R2SyncPlan(
    IReadOnlyList<R2SyncObjectPlan> Objects,
    long TotalBytes);

public sealed record R2SyncProgress(
    int Completed,
    int Total,
    int Active,
    int Uploaded,
    int Skipped,
    int Failed,
    int CachedHashes,
    long ProcessedBytes,
    long TotalBytes,
    long HashedBytes,
    long CachedHashBytes,
    long UploadedBytes,
    string Message);

public sealed record R2SyncResult(
    string RootDirectory,
    int ObjectCount,
    long TotalBytes,
    int UploadedCount,
    int SkippedCount,
    int CachedHashCount,
    int SeededHashCount,
    long ProcessedBytes,
    long UploadedBytes,
    bool DryRun,
    string? MappingManifestPath,
    string HashCachePath);

internal sealed record R2UploadEntry(
    string LocalPath,
    string ObjectKey,
    long Length,
    string ContentType,
    string? ContentEncoding);

public sealed partial class R2AssetSyncService
{
    private const long MaxObjectSize = 5L * 1024 * 1024 * 1024;

    public R2SyncPlan BuildPlan(R2SyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        var root = ResolveRootDirectory(options.RootDirectory);
        var entries = DiscoverEntries(root, options);
        if (entries.Count == 0)
        {
            throw new InvalidOperationException(
                $"在“{Path.Combine(root, "assets")}”下没有找到可同步的主数据、目录清单或资源文件。");
        }

        return new R2SyncPlan(
            entries.Select(static item => new R2SyncObjectPlan(
                item.LocalPath,
                item.ObjectKey,
                item.Length,
                item.ContentType,
                item.ContentEncoding)).ToArray(),
            entries.Sum(static item => item.Length));
    }

    public async Task<R2SyncResult> SyncAsync(
        R2SyncOptions options,
        IProgress<R2SyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        var root = ResolveRootDirectory(options.RootDirectory);
        var entries = DiscoverEntries(root, options);
        if (entries.Count == 0)
        {
            throw new InvalidOperationException(
                $"在“{Path.Combine(root, "assets")}”下没有找到可同步的主数据、目录清单或资源文件。");
        }

        var totalBytes = entries.Sum(static item => item.Length);
        progress?.Report(new R2SyncProgress(
            0, entries.Count, 0, 0, 0, 0, 0, 0, totalBytes, 0, 0, 0,
            $"已发现 {entries.Count} 个对象，共 {totalBytes:N0} 字节。"));
        if (options.DryRun)
        {
            var mappingPath = await WriteMappingManifestAsync(root, entries, cancellationToken);
            progress?.Report(new R2SyncProgress(
                entries.Count, entries.Count, 0, 0, 0, 0, 0, totalBytes, totalBytes, 0, 0, 0,
                $"预览完成，映射清单：{mappingPath}"));
            return new R2SyncResult(
                root,
                entries.Count,
                totalBytes,
                0,
                0,
                0,
                0,
                totalBytes,
                0,
                true,
                mappingPath,
                Path.Combine(root, "assets", "r2-hash-cache.json"));
        }

        ValidateCredentials(options);
        using var store = new R2S3ObjectStoreClient(
            options.Endpoint,
            options.Bucket,
            options.AccessKeyId!,
            options.SecretAccessKey!,
            options.SessionToken);
        var hashCache = R2FileHashCache.Load(root);
        var seeded = hashCache.SeedFromAssetManifests(root);
        progress?.Report(new R2SyncProgress(
            0, entries.Count, 0, 0, 0, 0, 0, 0, totalBytes, 0, 0, 0,
            $"哈希缓存：已有 {hashCache.Count} 条，来自 CDN 清单补充 {seeded} 条。"));

        var completed = 0;
        var active = 0;
        var uploaded = 0;
        var skipped = 0;
        var cachedHashes = 0;
        long hashedBytes = 0;
        long cachedHashBytes = 0;
        long uploadedBytes = 0;
        long processedBytes = 0;
        var failures = new ConcurrentBag<string>();
        using var progressCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progressTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(progressCancellation.Token))
            {
                ReportProgress();
            }
        }, CancellationToken.None);
        try
        {
            await Parallel.ForEachAsync(
                entries,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = options.Concurrency,
                    CancellationToken = cancellationToken
                },
                async (entry, ct) =>
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        string sha256;
                        var hasCachedHash = hashCache.TryGet(entry, out var cachedSha256, out _);
                        if (hasCachedHash)
                        {
                            sha256 = cachedSha256;
                            Interlocked.Increment(ref cachedHashes);
                            Interlocked.Add(ref cachedHashBytes, entry.Length);
                        }
                        else
                        {
                            sha256 = await HashFileAsync(entry.LocalPath, ct);
                            Interlocked.Add(ref hashedBytes, entry.Length);
                        }

                        var hasRemoteBaseline = hashCache.HasRemoteVerifiedBaseline(entry);
                        if (options.OnlyUploadChanged && !options.Force && hasCachedHash && hasRemoteBaseline)
                        {
                            Interlocked.Increment(ref skipped);
                            return;
                        }

                        if (options.OnlyUploadChanged && !options.Force && !hasCachedHash && hasRemoteBaseline)
                        {
                            await ExecuteWithRetryAsync(
                                innerCt => store.PutAsync(entry, sha256, innerCt),
                                options.MaxRetries,
                                ct);
                            Interlocked.Increment(ref uploaded);
                            Interlocked.Add(ref uploadedBytes, entry.Length);
                            hashCache.Set(entry, sha256, remoteVerified: true);
                            return;
                        }

                        var unchanged = !options.Force && await ExecuteWithRetryAsync(
                            innerCt => store.IsCurrentAsync(
                                entry.ObjectKey,
                                entry.Length,
                                sha256,
                                entry.ContentEncoding,
                                innerCt),
                            options.MaxRetries,
                            ct);
                        if (unchanged)
                        {
                            Interlocked.Increment(ref skipped);
                            hashCache.Set(entry, sha256, remoteVerified: true);
                        }
                        else
                        {
                            await ExecuteWithRetryAsync(
                                innerCt => store.PutAsync(entry, sha256, innerCt),
                                options.MaxRetries,
                                ct);
                            Interlocked.Increment(ref uploaded);
                            Interlocked.Add(ref uploadedBytes, entry.Length);
                            hashCache.Set(entry, sha256, remoteVerified: true);
                        }
                    }
                    catch (HttpRequestException exception) when (
                        exception.StatusCode is { } status &&
                        (int)status < 500 &&
                        status != System.Net.HttpStatusCode.TooManyRequests)
                    {
                        throw;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        failures.Add($"{entry.ObjectKey}：{exception.Message}");
                    }
                    finally
                    {
                        Interlocked.Add(ref processedBytes, entry.Length);
                        Interlocked.Increment(ref completed);
                        Interlocked.Decrement(ref active);
                        ReportProgress();
                    }
                });
        }
        finally
        {
            await progressCancellation.CancelAsync();
            try
            {
                await progressTask;
            }
            catch (OperationCanceledException) when (progressCancellation.IsCancellationRequested)
            {
            }

            try
            {
                await hashCache.SaveAsync(CancellationToken.None);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                progress?.Report(new R2SyncProgress(
                    Volatile.Read(ref completed), entries.Count, Volatile.Read(ref active),
                    Volatile.Read(ref uploaded), Volatile.Read(ref skipped), failures.Count,
                    Volatile.Read(ref cachedHashes), Interlocked.Read(ref processedBytes), totalBytes,
                    Interlocked.Read(ref hashedBytes), Interlocked.Read(ref cachedHashBytes),
                    Interlocked.Read(ref uploadedBytes), $"警告：哈希缓存保存失败：{exception.Message}"));
            }
        }

        if (!failures.IsEmpty)
        {
            var examples = failures.OrderBy(item => item, StringComparer.Ordinal).Take(20);
            throw new IOException(
                $"R2 同步失败，共 {failures.Count} 个对象。{Environment.NewLine}{string.Join(Environment.NewLine, examples)}");
        }

        ReportProgress();
        return new R2SyncResult(
            root,
            entries.Count,
            totalBytes,
            uploaded,
            skipped,
            cachedHashes,
            seeded,
            processedBytes,
            uploadedBytes,
            false,
            null,
            Path.Combine(root, "assets", "r2-hash-cache.json"));

        void ReportProgress()
        {
            progress?.Report(new R2SyncProgress(
                Volatile.Read(ref completed),
                entries.Count,
                Volatile.Read(ref active),
                Volatile.Read(ref uploaded),
                Volatile.Read(ref skipped),
                failures.Count,
                Volatile.Read(ref cachedHashes),
                Interlocked.Read(ref processedBytes),
                totalBytes,
                Interlocked.Read(ref hashedBytes),
                Interlocked.Read(ref cachedHashBytes),
                Interlocked.Read(ref uploadedBytes),
                $"同步进度：{Volatile.Read(ref completed)}/{entries.Count}，上传 {Volatile.Read(ref uploaded)}，跳过 {Volatile.Read(ref skipped)}，本地缓存命中 {Volatile.Read(ref cachedHashes)}，失败 {failures.Count}，已处理 {Interlocked.Read(ref processedBytes):N0}/{totalBytes:N0} 字节"));
        }
    }

    private static async Task<string> WriteMappingManifestAsync(
        string root,
        IReadOnlyList<R2UploadEntry> entries,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, "assets", "r2-object-map.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var writer = new StreamWriter(stream);
        await writer.WriteLineAsync("local_path\tobject_key\tlength\tcontent_type\tcontent_encoding");
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var localPath = Path.GetRelativePath(root, entry.LocalPath).Replace('\\', '/');
            await writer.WriteLineAsync(
                $"{localPath}\t{entry.ObjectKey}\t{entry.Length}\t{entry.ContentType}\t{entry.ContentEncoding ?? string.Empty}");
        }

        await writer.FlushAsync(cancellationToken);
        return path;
    }

    private static List<R2UploadEntry> DiscoverEntries(string root, R2SyncOptions options)
    {
        var entries = new Dictionary<string, R2UploadEntry>(StringComparer.Ordinal);
        AddMasterDataEntry(root, options, entries);
        AddCatalogEntries(root, options, entries);
        var customMappings = NormalizeCustomMappings(root, options.CustomMappings);
        AddAssetFileEntries(root, options, entries, customMappings);
        return entries.Values.OrderBy(item => item.ObjectKey, StringComparer.Ordinal).ToList();
    }

    private static string ResolveRootDirectory(string rootValue)
    {
        var root = Path.GetFullPath(rootValue);
        var directory = new DirectoryInfo(root);
        return string.Equals(directory.Name, "assets", StringComparison.OrdinalIgnoreCase) && directory.Parent is not null
            ? directory.Parent.FullName
            : root;
    }

    private static void AddMasterDataEntry(string root, R2SyncOptions options, IDictionary<string, R2UploadEntry> entries)
    {
        var masterRoot = Path.Combine(root, "master");
        var manifestPath = Path.Combine(masterRoot, "manifest.json");
        var databasePath = Path.Combine(masterRoot, "mastermemory.db");
        if (!File.Exists(manifestPath) && !File.Exists(databasePath))
            return;
        if (!File.Exists(manifestPath) || !File.Exists(databasePath))
            throw new InvalidDataException($"主数据发布需要同时存在“{manifestPath}”和“{databasePath}”。");

        using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        if (!document.RootElement.TryGetProperty("Uri", out var uriElement) ||
            uriElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(uriElement.GetString()))
            throw new InvalidDataException($"主数据清单没有有效的 Uri：{manifestPath}");

        var relativeUri = uriElement.GetString()!.Replace('\\', '/').Trim('/');
        if (Uri.TryCreate(relativeUri, UriKind.Absolute, out _) ||
            relativeUri.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(item => item is "." or ".."))
            throw new InvalidDataException($"主数据清单中的 Uri 无效：{relativeUri}");
        var key = relativeUri.StartsWith("master-data/production/", StringComparison.Ordinal)
            ? relativeUri
            : CombineObjectKey("master-data", "production", relativeUri);
        AddEntry(root, options, entries, databasePath, key, "application/octet-stream", null);
    }

    private static void AddCatalogEntries(string root, R2SyncOptions options, IDictionary<string, R2UploadEntry> entries)
    {
        var catalogRoot = Path.Combine(root, "assets", "catalogs");
        if (!Directory.Exists(catalogRoot))
            return;
        foreach (var path in Directory.EnumerateFiles(catalogRoot, "*", SearchOption.AllDirectories))
        {
            if (IsWorkFile(path))
                continue;
            var relative = Path.GetRelativePath(catalogRoot, path);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Length < 2)
                continue;
            var match = CatalogFileName().Match(parts[^1]);
            if (!match.Success)
                continue;
            var category = parts[0];
            var platform = parts.Length == 2 ? "Android" : parts[1];
            var version = match.Groups["version"].Value;
            var key = CombineObjectKey("production", category, platform, version, parts[^1]);
            AddEntry(root, options, entries, path, key, CatalogContentType(parts[^1]), null);
        }
    }

    private static void AddAssetFileEntries(
        string root,
        R2SyncOptions options,
        IDictionary<string, R2UploadEntry> entries,
        IReadOnlyList<NormalizedR2CustomMapping> customMappings)
    {
        var assetRoot = Path.Combine(root, "assets", "files");
        if (!Directory.Exists(assetRoot))
            return;
        foreach (var path in Directory.EnumerateFiles(assetRoot, "*", SearchOption.AllDirectories))
        {
            if (IsWorkFile(path))
                continue;
            var relative = Path.GetRelativePath(assetRoot, path);
            var relativeKey = relative.Replace('\\', '/');
            if (TryFindCustomMapping(relativeKey, customMappings, out var customMapping, out var customSuffix))
            {
                var customKey = string.IsNullOrEmpty(customSuffix)
                    ? customMapping.ObjectPrefix
                    : CombineObjectKey(customMapping.ObjectPrefix, customSuffix);
                AddEntry(root, options, entries, path, customKey, AssetContentType(path), null);
                continue;
            }

            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var mappedPath = parts.Length >= 3 && LooksLikeOriginHost(parts[1])
                ? CombineObjectKey(parts.Skip(2).ToArray())
                : CombineObjectKey(parts);
            if (mappedPath.StartsWith("scenes/", StringComparison.OrdinalIgnoreCase))
                mappedPath = CombineObjectKey("master-data", "production", mappedPath);
            if (mappedPath.StartsWith("notations/", StringComparison.OrdinalIgnoreCase))
                mappedPath = $"Notations/{mappedPath["notations/".Length..]}";
            var key = mappedPath.StartsWith("production/", StringComparison.Ordinal) ||
                      mappedPath.StartsWith("master-data/", StringComparison.Ordinal)
                ? mappedPath
                : CombineObjectKey("production", mappedPath);
            AddEntry(root, options, entries, path, key, AssetContentType(path), null);
        }
    }

    private static IReadOnlyList<NormalizedR2CustomMapping> NormalizeCustomMappings(
        string root,
        IReadOnlyList<R2CustomMapping>? mappings)
    {
        if (mappings is null || mappings.Count == 0)
            return Array.Empty<NormalizedR2CustomMapping>();

        var assetRoot = Path.Combine(root, "assets", "files");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<NormalizedR2CustomMapping>(mappings.Count);
        foreach (var mapping in mappings)
        {
            var localDirectory = NormalizeRelativePath(mapping.LocalDirectory, "自定义映射本地目录");
            var objectPrefix = NormalizeRelativePath(mapping.ObjectPrefix, "自定义映射 R2 对象前缀");
            if (!seen.Add(localDirectory))
                throw new InvalidDataException($"自定义映射本地目录重复：{localDirectory}");

            var fullDirectory = Path.GetFullPath(Path.Combine(
                assetRoot,
                localDirectory.Replace('/', Path.DirectorySeparatorChar)));
            EnsureBelowRoot(assetRoot, fullDirectory);
            if (!Directory.Exists(fullDirectory))
                throw new DirectoryNotFoundException($"自定义映射目录不存在：{fullDirectory}");

            normalized.Add(new NormalizedR2CustomMapping(localDirectory, objectPrefix));
        }

        return normalized
            .OrderByDescending(item => item.LocalDirectory.Length)
            .ToArray();
    }

    private static bool TryFindCustomMapping(
        string relativePath,
        IReadOnlyList<NormalizedR2CustomMapping> mappings,
        out NormalizedR2CustomMapping mapping,
        out string suffix)
    {
        foreach (var candidate in mappings)
        {
            if (relativePath.Equals(candidate.LocalDirectory, StringComparison.OrdinalIgnoreCase))
            {
                mapping = candidate;
                suffix = string.Empty;
                return true;
            }

            var prefix = candidate.LocalDirectory + "/";
            if (relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                mapping = candidate;
                suffix = relativePath[prefix.Length..];
                return true;
            }
        }

        mapping = null!;
        suffix = string.Empty;
        return false;
    }

    private static string NormalizeRelativePath(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{description}不能为空。");

        var normalized = value.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(value) || normalized.StartsWith("/", StringComparison.Ordinal) ||
            Uri.TryCreate(normalized, UriKind.Absolute, out _) ||
            normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(item => item is "." or ".."))
            throw new InvalidDataException($"{description}必须是安全的相对路径：{value}");

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            throw new InvalidDataException($"{description}不能为空。");
        return string.Join('/', parts);
    }

    private sealed record NormalizedR2CustomMapping(string LocalDirectory, string ObjectPrefix);

    private static bool LooksLikeOriginHost(string value) =>
        value.Contains('.', StringComparison.Ordinal) ||
        value.Equals("localhost", StringComparison.OrdinalIgnoreCase);

    private static void AddEntry(
        string root,
        R2SyncOptions options,
        IDictionary<string, R2UploadEntry> entries,
        string localPath,
        string objectKey,
        string contentType,
        string? contentEncoding)
    {
        var fullPath = Path.GetFullPath(localPath);
        EnsureBelowRoot(root, fullPath);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
            throw new FileNotFoundException("R2 源文件不存在。", fullPath);
        if (info.Length > MaxObjectSize)
            throw new InvalidDataException($"文件超过单对象 5 GiB 限制：{fullPath}");

        var normalizedKey = ApplyPrefix(options.KeyPrefix, objectKey);
        var incoming = new R2UploadEntry(fullPath, normalizedKey, info.Length, contentType, contentEncoding);
        if (entries.TryGetValue(normalizedKey, out var existing) &&
            !string.Equals(existing.LocalPath, incoming.LocalPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"两个本地文件映射到了同一个 R2 对象键“{normalizedKey}”：{existing.LocalPath}、{incoming.LocalPath}。");
        }
        entries[normalizedKey] = incoming;
    }

    private static string ApplyPrefix(string prefixValue, string key)
    {
        var prefix = (prefixValue ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
        if (prefix.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(item => item is "." or ".."))
            throw new ArgumentException("R2 对象前缀不能包含 . 或 ..。", nameof(prefixValue));
        return string.IsNullOrEmpty(prefix) ? key : $"{prefix}/{key}";
    }

    private static string CombineObjectKey(params string[] parts) =>
        string.Join('/', parts
            .SelectMany(item => item.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
            .Select(Uri.UnescapeDataString));

    private static void EnsureBelowRoot(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidDataException($"R2 源文件路径越过输出目录：{path}");
    }

    private static async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> action,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0;; attempt++)
        {
            try
            {
                return await action(cancellationToken);
            }
            catch (Exception exception) when (
                attempt < maxRetries &&
                exception is not OperationCanceledException &&
                IsRetryable(exception))
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt + 1))), cancellationToken);
            }
        }
    }

    private static async Task ExecuteWithRetryAsync(
        Func<CancellationToken, Task> action,
        int maxRetries,
        CancellationToken cancellationToken)
        => await ExecuteWithRetryAsync(async ct =>
        {
            await action(ct);
            return true;
        }, maxRetries, cancellationToken);

    private static bool IsRetryable(Exception exception) => exception switch
    {
        IOException => true,
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: { } status } =>
            (int)status == 408 || (int)status == 429 || (int)status >= 500,
        _ => false
    };

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static bool IsWorkFile(string path) =>
        path.EndsWith(".bck", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

    private static string CatalogContentType(string path) =>
        path.EndsWith(".hash", StringComparison.OrdinalIgnoreCase)
            ? "text/plain; charset=utf-8"
            : path.EndsWith(".br", StringComparison.OrdinalIgnoreCase)
                ? "application/octet-stream"
                : "application/json";

    private static string AssetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "application/json",
        ".txt" or ".hash" => "text/plain; charset=utf-8",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".acb" or ".awb" or ".bundle" => "application/octet-stream",
        _ => "application/octet-stream"
    };

    private static void ValidateOptions(R2SyncOptions options)
    {
        if (!Uri.TryCreate(options.Endpoint?.Trim(), UriKind.Absolute, out var endpoint) ||
            (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("R2 地址必须是 HTTP(S) 绝对地址。", nameof(options));
        if (string.IsNullOrWhiteSpace(options.Bucket))
            throw new ArgumentException("R2 存储桶不能为空。", nameof(options));
        if (options.Concurrency <= 0)
            throw new ArgumentException("并发数必须大于 0。", nameof(options));
        if (options.MaxRetries < 0)
            throw new ArgumentException("重试次数不能小于 0。", nameof(options));
    }

    private static void ValidateCredentials(R2SyncOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AccessKeyId) ||
            string.IsNullOrWhiteSpace(options.SecretAccessKey))
            throw new InvalidOperationException("缺少 R2 凭据，请填写访问密钥 ID 和秘密访问密钥，或设置 R2_ACCESS_KEY_ID、R2_SECRET_ACCESS_KEY 环境变量。");
    }

    [GeneratedRegex(@"^catalog_(?<version>.+)\.(?:json(?:\.br)?|hash)$", RegexOptions.IgnoreCase)]
    private static partial Regex CatalogFileName();
}
