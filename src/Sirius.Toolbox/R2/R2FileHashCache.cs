using System.Collections.Concurrent;
using System.Text.Json;

namespace Sirius.Toolbox.R2;

internal sealed class R2FileHashCache
{
    private const int SchemaVersion = 1;
    private readonly string _root;
    private readonly string _path;
    private readonly ConcurrentDictionary<string, R2FileHashCacheEntry> _entries;

    private R2FileHashCache(
        string root,
        string path,
        ConcurrentDictionary<string, R2FileHashCacheEntry> entries)
    {
        _root = root;
        _path = path;
        _entries = entries;
    }

    public int Count => _entries.Count;

    public static R2FileHashCache Load(string root)
    {
        root = Path.GetFullPath(root);
        var path = Path.Combine(root, "assets", "r2-hash-cache.json");
        try
        {
            if (!File.Exists(path))
                return Empty(root, path);
            var document = JsonSerializer.Deserialize<R2FileHashCacheDocument>(File.ReadAllBytes(path));
            if (document is null || document.Version != SchemaVersion)
                return Empty(root, path);
            return new R2FileHashCache(
                root,
                path,
                new ConcurrentDictionary<string, R2FileHashCacheEntry>(
                    document.Files,
                    StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"警告：无法读取 R2 哈希缓存：{exception.Message}");
                return Empty(root, path);
            }
    }

    public bool TryGet(R2UploadEntry entry, out string sha256)
        => TryGet(entry, out sha256, out _);

    public bool TryGet(R2UploadEntry entry, out string sha256, out bool remoteVerified)
    {
        var info = new FileInfo(entry.LocalPath);
        var key = GetKey(entry.LocalPath);
        if (_entries.TryGetValue(key, out var cached) &&
            cached.Length == info.Length &&
            cached.LastWriteTimeUtcTicks == info.LastWriteTimeUtc.Ticks &&
            cached.Sha256.Length == 64)
        {
            sha256 = cached.Sha256;
            remoteVerified = cached.RemoteVerified;
            return true;
        }

        sha256 = string.Empty;
        remoteVerified = false;
        return false;
    }

    public bool HasRemoteVerifiedBaseline(R2UploadEntry entry)
    {
        var key = GetKey(entry.LocalPath);
        return _entries.TryGetValue(key, out var cached) && cached.RemoteVerified;
    }

    public void Set(R2UploadEntry entry, string sha256, bool remoteVerified = false)
    {
        var info = new FileInfo(entry.LocalPath);
        _entries[GetKey(entry.LocalPath)] = new R2FileHashCacheEntry
        {
            Length = info.Length,
            LastWriteTimeUtcTicks = info.LastWriteTimeUtc.Ticks,
            Sha256 = sha256,
            RemoteVerified = remoteVerified
        };
    }

    public int SeedFromAssetManifests(string root)
    {
        var seeded = 0;
        var manifestRoot = Path.Combine(root, "assets", "manifests");
        if (!Directory.Exists(manifestRoot))
            return 0;

        foreach (var manifestPath in Directory.EnumerateFiles(manifestRoot, "cdn_*.json"))
        {
            R2AssetManifestDocument? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<R2AssetManifestDocument>(File.ReadAllBytes(manifestPath));
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                continue;
            }

            if (manifest?.Objects is null)
                continue;
            foreach (var record in manifest.Objects.Values)
            {
                if (record.Status != R2AssetStatus.Complete ||
                    string.IsNullOrWhiteSpace(record.Sha256) || record.Sha256.Length != 64)
                    continue;

                var localPath = Path.Combine(root, "assets", "files",
                    record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var info = new FileInfo(localPath);
                if (!info.Exists || info.Length != record.DownloadedSize)
                    continue;

                var key = GetKey(localPath);
                if (_entries.ContainsKey(key))
                    continue;
                _entries[key] = new R2FileHashCacheEntry
                {
                    Length = info.Length,
                    LastWriteTimeUtcTicks = info.LastWriteTimeUtc.Ticks,
                    Sha256 = record.Sha256.ToLowerInvariant(),
                    RemoteVerified = false
                };
                seeded++;
            }
        }

        return seeded;
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        var document = new R2FileHashCacheDocument
        {
            Version = SchemaVersion,
            Files = _entries.OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase)
        };
        var parent = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);
        var temporaryPath = $"{_path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, document, cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private string GetKey(string path) =>
        Path.GetRelativePath(_root, path).Replace('\\', '/');

    private static R2FileHashCache Empty(string root, string path) =>
        new(root, path, new ConcurrentDictionary<string, R2FileHashCacheEntry>(StringComparer.OrdinalIgnoreCase));
}

internal sealed class R2FileHashCacheDocument
{
    public int Version { get; set; }
    public Dictionary<string, R2FileHashCacheEntry> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class R2FileHashCacheEntry
{
    public long Length { get; set; }
    public long LastWriteTimeUtcTicks { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public bool RemoteVerified { get; set; }
}

internal sealed class R2AssetManifestDocument
{
    public Dictionary<string, R2AssetManifestObject> Objects { get; set; } = new(StringComparer.Ordinal);
}

internal enum R2AssetStatus
{
    Pending,
    Downloading,
    Complete,
    Failed,
    NotFound
}

internal sealed class R2AssetManifestObject
{
    public string RelativePath { get; set; } = string.Empty;
    public long DownloadedSize { get; set; }
    public string? Sha256 { get; set; }
    public R2AssetStatus Status { get; set; }
}
