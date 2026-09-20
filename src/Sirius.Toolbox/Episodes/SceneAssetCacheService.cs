using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using Sirius.Toolbox.Episodes;
using Sirius.Toolbox.IO;

namespace Sirius.Toolbox.Episodes;

public sealed record SceneAssetCacheOptions(
    bool MetadataOnly,
    string? MasterDataVersion,
    string? SourceRevision,
    string EpisodePathPrefix = "episode",
    string ScenePathPrefix = "scenes");

public sealed record SceneAssetCacheEntry(
    long EpisodeMasterId,
    string RelativePath,
    string FileName,
    string Sha256,
    string GitObjectId,
    string HashAlgorithm,
    string SourcePath,
    bool MetadataOnly,
    long FileSize,
    long LastWriteTimeUtcTicks);

public sealed record SceneAssetCacheDocument(
    int Version,
    string MasterDataVersion,
    string SourceRevision,
    DateTimeOffset GeneratedAt,
    IReadOnlyDictionary<string, SceneAssetCacheEntry> Assets);

public sealed record SceneAssetCacheBuildResult(
    string OutputPath,
    int AssetCount,
    int MatchedJsonCount,
    int MissingJsonCount,
    long TotalBytes);

public sealed class SceneAssetCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public SceneAssetCacheBuildResult Build(
        string episodeDirectory,
        string sceneDirectory,
        string outputPath,
        SceneAssetCacheOptions options,
        bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(options);

        var fullEpisodeDirectory = RequireDirectory(episodeDirectory, "Episode JSON");
        var fullSceneDirectory = RequireDirectory(sceneDirectory, "scene BIN");
        var fullOutputPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullOutputPath) && !overwrite)
            throw new IOException($"缓存文件已存在：{fullOutputPath}。请启用覆盖选项。" );

        var jsonById = ReadEpisodeSources(fullEpisodeDirectory);
        var sceneFiles = Directory.EnumerateFiles(fullSceneDirectory, "*.bin", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sceneFiles.Length == 0)
            throw new InvalidDataException("scene BIN 目录中没有 .bin 文件。" );

        var entries = new Dictionary<string, SceneAssetCacheEntry>(StringComparer.Ordinal);
        var matchedJsonCount = 0;
        long totalBytes = 0;

        foreach (var scenePath in sceneFiles)
        {
            var episodeId = ParseNumericFileId(scenePath, "BIN");
            var key = episodeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (entries.ContainsKey(key))
            {
                var previous = entries[key].RelativePath;
                var current = ToCachePath(options.ScenePathPrefix, Path.GetRelativePath(fullSceneDirectory, scenePath));
                throw new InvalidDataException($"发现重复 Episode ID {key}：{previous} 和 {current}。" );
            }

            var fileInfo = new FileInfo(scenePath);
            var hasSource = jsonById.TryGetValue(episodeId, out var source);
            if (hasSource) matchedJsonCount++;

            var sha256 = options.MetadataOnly ? string.Empty : ComputeSha256(scenePath);
            var relativePath = ToCachePath(options.ScenePathPrefix, Path.GetRelativePath(fullSceneDirectory, scenePath));
            var sourcePath = hasSource
                ? ToCachePath(options.EpisodePathPrefix, source!.RelativePath)
                : string.Empty;
            var entry = new SceneAssetCacheEntry(
                episodeId,
                relativePath,
                fileInfo.Name,
                sha256,
                string.Empty,
                options.MetadataOnly ? string.Empty : "sha256",
                sourcePath,
                options.MetadataOnly,
                fileInfo.Length,
                options.MetadataOnly ? 0 : fileInfo.LastWriteTimeUtc.Ticks);
            entries.Add(key, entry);
            totalBytes += fileInfo.Length;
        }

        var orderedEntries = entries
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        var document = new SceneAssetCacheDocument(
            2,
            options.MasterDataVersion ?? string.Empty,
            options.SourceRevision ?? string.Empty,
            DateTimeOffset.UtcNow,
            orderedEntries);
        var json = JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;
        AtomicFile.WriteAllText(fullOutputPath, json);

        return new SceneAssetCacheBuildResult(
            fullOutputPath,
            orderedEntries.Count,
            matchedJsonCount,
            orderedEntries.Count - matchedJsonCount,
            totalBytes);
    }

    public SceneAssetCacheDocument Load(string cachePath)
    {
        var fullPath = Path.GetFullPath(cachePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("找不到 scene-assets.json。", fullPath);

        var json = File.ReadAllText(fullPath, new UTF8Encoding(false, true));
        var document = JsonSerializer.Deserialize<SceneAssetCacheDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("缓存文件为空。" );
        if (document.Version != 2)
            throw new InvalidDataException($"不支持的 scene-assets.json Version：{document.Version}。" );
        if (document.Assets is null)
            throw new InvalidDataException("缓存文件缺少 Assets。" );

        foreach (var pair in document.Assets)
        {
            if (!long.TryParse(pair.Key, out var keyId) || keyId != pair.Value.EpisodeMasterId)
                throw new InvalidDataException($"缓存键与 EpisodeMasterId 不一致：{pair.Key}。" );
        }

        return document;
    }

    private static Dictionary<long, EpisodeSource> ReadEpisodeSources(string directory)
    {
        var sources = new Dictionary<long, EpisodeSource>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            EpisodeInput input;
            try
            {
                input = EpisodeCodec.ReadJson(path);
            }
            catch (UnsupportedEpisodeFormatException)
            {
                // StoryType=5 files are PosterStoryMaster records from master
                // data, not scene BIN sources. They must not become fake scene
                // cache entries or be packed with an empty Phrase field.
                continue;
            }

            var episodeId = input.EpisodeId;
            if (episodeId <= 0)
                episodeId = ParseNumericFileId(path, "JSON");

            if (sources.ContainsKey(episodeId))
            {
                var previous = sources[episodeId].RelativePath;
                var current = Path.GetRelativePath(directory, path);
                throw new InvalidDataException($"发现重复 Episode ID {episodeId}：{previous} 和 {current}。" );
            }

            sources.Add(episodeId, new EpisodeSource(Path.GetRelativePath(directory, path)));
        }

        return sources;
    }

    private static string RequireDirectory(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"请提供 {label} 目录。", nameof(path));
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"找不到 {label} 目录：{fullPath}");
        return fullPath;
    }

    private static long ParseNumericFileId(string path, string kind)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (!long.TryParse(fileName, out var id) || id <= 0)
            throw new InvalidDataException($"{kind} 文件名必须是正整数 ID：{path}");
        return id;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ToCachePath(string prefix, string relativePath)
    {
        var normalizedPrefix = (prefix ?? string.Empty).Replace('\\', '/').Trim('/');
        var normalizedRelative = relativePath.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(normalizedPrefix)
            ? normalizedRelative
            : $"{normalizedPrefix}/{normalizedRelative}";
    }

    private sealed record EpisodeSource(string RelativePath);
}
