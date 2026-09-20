using System.Text.Json;

namespace Sirius.Toolbox.Assets;

/// <summary>
/// static-assets 发现选项。
/// Options for static-asset discovery.
/// </summary>
public sealed record StaticAssetDiscoveryOptions
{
    public required string RootDirectory { get; init; }
    public string? AssetDirectory { get; init; }
    public required string StaticContentBaseUrl { get; init; }
    public Action<string>? Log { get; init; }
}

/// <summary>
/// 从主数据与既有文件推导 static-assets 下载清单。规则与旧 Sirius.AssetTool 的
/// DiscoverStaticAssetObjects 一致：Banner / Gacha / Splash / Comic / 既有文件 / 种子文件。
/// Derives the static-asset download set from master data and cached files. The rules match the
/// retired Sirius.AssetTool DiscoverStaticAssetObjects implementation.
/// </summary>
public static class StaticAssetDiscovery
{
    public const string Category = CdnAssetMirrorOptions.StaticAssetsCategory;

    public static IReadOnlyList<CdnAssetObjectRecord> Discover(StaticAssetDiscoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = Path.GetFullPath(options.RootDirectory);
        var assetRoot = string.IsNullOrWhiteSpace(options.AssetDirectory)
            ? Path.Combine(root, "assets")
            : Path.GetFullPath(options.AssetDirectory);
        var staticBase = options.StaticContentBaseUrl.TrimEnd('/');
        var log = options.Log;
        var paths = new HashSet<string>(StringComparer.Ordinal);

        AddBannerPaths(root, paths, log);
        AddGachaBannerPaths(root, paths, log);
        AddInformationPaths(root, paths, log);
        AddComicPaths(root, paths, log);
        AddCachedPaths(assetRoot, paths);
        AddSeedPaths(assetRoot, paths, log);

        if (paths.Count == 0)
            return [];

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var staticRoot = Path.Combine(assetRoot, "files", "static-assets");
        return [.. paths
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path =>
            {
                var localPath = Path.Combine(staticRoot, path.Replace('/', Path.DirectorySeparatorChar));
                var info = new FileInfo(localPath);
                var exists = info.Exists;
                return new CdnAssetObjectRecord
                {
                    Url = $"{staticBase}/{path}",
                    RelativePath = $"{Category}/{path}",
                    Category = Category,
                    ExpectedSize = exists ? info.Length : 0,
                    DownloadedSize = exists ? info.Length : 0,
                    LastModified = exists
                        ? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero).ToString("O")
                        : null,
                    Status = exists ? CdnAssetStatus.Complete : CdnAssetStatus.Pending,
                    UpdatedAtUnixMs = now
                };
            })];
    }

    private static void AddBannerPaths(string root, ISet<string> paths, Action<string>? log)
    {
        var bannerMasterPath = Path.Combine(root, "master", "json", "BannerMaster.json");
        if (!File.Exists(bannerMasterPath))
        {
            log?.Invoke("warning: BannerMaster.json 不可用；static-assets 只使用缓存文件与种子文件。");
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(bannerMasterPath));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("BannerMaster.json 根节点必须是数组。");

            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (!JsonProbe.TryGetPropertyOrArrayElement(row, "ImagePath", 9, out var imagePathElement)
                    || imagePathElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var imagePath = imagePathElement.GetString()?.Trim().Replace('\\', '/').Trim('/');
                if (string.IsNullOrWhiteSpace(imagePath) || !AssetCatalogParser.IsSafeRelativePath(imagePath))
                {
                    log?.Invoke($"warning: BannerMaster 含有不安全的 ImagePath，已跳过：{imagePathElement.GetString()}");
                    continue;
                }

                paths.Add($"Resources/Textures/Banners/{imagePath}.astc.gz");
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            log?.Invoke($"warning: BannerMaster static 资源发现失败：{exception.Message}");
        }
    }

    private static void AddGachaBannerPaths(string root, ISet<string> paths, Action<string>? log)
    {
        var discovered = AddMasterKeysAsPaths(
            Path.Combine(root, "master", "json", "GachaMaster.json"),
            "Id",
            arrayIndex: 0,
            value => $"Resources/Textures/Banners/Gacha/{value}.astc.gz",
            log);
        foreach (var path in discovered)
            paths.Add(path);
        log?.Invoke($"static-assets: gacha_banners={discovered.Count}");
    }

    private static void AddInformationPaths(string root, ISet<string> paths, Action<string>? log)
    {
        var masterPath = Path.Combine(root, "master", "json", "SplashMaster.json");
        if (!File.Exists(masterPath))
        {
            log?.Invoke("warning: SplashMaster.json 不可用；信息图 static 资源发现已跳过。");
            return;
        }

        var discovered = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(masterPath));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("SplashMaster.json 根节点必须是数组。");

            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (!JsonProbe.TryGetPropertyOrArrayElement(row, "SplashType", 2, out var type)
                    || !string.Equals(type.ToString(), "Picture", StringComparison.OrdinalIgnoreCase)
                    || !JsonProbe.TryGetPropertyOrArrayElement(row, "SplashValue", 3, out var value))
                {
                    continue;
                }

                var key = value.ToString().Trim();
                if (key.Length == 0 || !IsSafePathSegment(key))
                    continue;
                discovered.Add($"Resources/Textures/Banners/Information/{key}.astc.gz");
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            log?.Invoke($"warning: 信息图 static 资源发现失败：{exception.Message}");
            return;
        }

        foreach (var path in discovered)
            paths.Add(path);
        log?.Invoke($"static-assets: information_images={discovered.Count}");
    }

    private static void AddComicPaths(string root, ISet<string> paths, Action<string>? log)
    {
        var comicMasterPath = Path.Combine(root, "master", "json", "ComicMaster.json");
        if (!File.Exists(comicMasterPath))
        {
            log?.Invoke("warning: ComicMaster.json 不可用；漫画 static 资源发现已跳过。");
            return;
        }

        var discovered = 0;
        var malformedBodies = 0;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(comicMasterPath));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("ComicMaster.json 根节点必须是数组。");

            foreach (var comic in document.RootElement.EnumerateArray())
            {
                if (!JsonProbe.TryGetPropertyOrArrayElement(comic, "Episodes", 2, out var episodes)
                    || episodes.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var episode in episodes.EnumerateArray())
                {
                    if (!JsonProbe.TryGetPropertyOrArrayElement(episode, "Body", 3, out var body))
                        continue;
                    try
                    {
                        if (AddComicBodyPaths(body, paths))
                            discovered++;
                    }
                    catch (JsonException)
                    {
                        malformedBodies++;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            log?.Invoke($"warning: 漫画 static 资源发现失败：{exception.Message}");
            return;
        }

        log?.Invoke($"static-assets: comic_images={discovered} malformed_bodies={malformedBodies}");
    }

    private static bool AddComicBodyPaths(JsonElement body, ISet<string> paths)
    {
        if (body.ValueKind == JsonValueKind.String)
        {
            var json = body.GetString();
            if (string.IsNullOrWhiteSpace(json))
                return false;
            using var document = JsonDocument.Parse(json);
            return AddComicBodyPaths(document.RootElement, paths);
        }

        if (body.ValueKind != JsonValueKind.Array)
            return false;

        var found = false;
        foreach (var element in body.EnumerateArray())
        {
            if (!JsonProbe.TryGetPropertyOrArrayElement(element, "Element", 0, out var type)
                || !JsonProbe.TryReadInt32(type, out var elementType)
                || elementType != 6
                || !JsonProbe.TryGetPropertyOrArrayElement(element, "Data", 1, out var data)
                || data.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var key = data.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(key) || !IsSafePathSegment(key))
                continue;
            paths.Add($"Resources/Textures/Comic/{key}.png");
            found = true;
        }

        return found;
    }

    private static IReadOnlyCollection<string> AddMasterKeysAsPaths(
        string masterPath,
        string propertyName,
        int arrayIndex,
        Func<string, string> pathFactory,
        Action<string>? log)
    {
        var discovered = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(masterPath))
        {
            log?.Invoke($"warning: {Path.GetFileName(masterPath)} 不可用；相关 static 资源发现已跳过。");
            return discovered;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(masterPath));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"{Path.GetFileName(masterPath)} 根节点必须是数组。");

            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (!JsonProbe.TryGetPropertyOrArrayElement(row, propertyName, arrayIndex, out var value))
                    continue;

                var key = value.ToString().Trim();
                if (key.Length == 0 || !IsSafePathSegment(key))
                    continue;
                discovered.Add(pathFactory(key));
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            log?.Invoke($"warning: {Path.GetFileName(masterPath)} static 资源发现失败：{exception.Message}");
        }

        return discovered;
    }

    private static void AddCachedPaths(string assetRoot, ISet<string> paths)
    {
        var staticRoot = Path.Combine(assetRoot, "files", "static-assets");
        if (!Directory.Exists(staticRoot))
            return;

        foreach (var file in Directory.EnumerateFiles(staticRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(staticRoot, file).Replace('\\', '/');
            if (AssetCatalogParser.IsSafeRelativePath(relative))
                paths.Add(relative);
        }
    }

    private static void AddSeedPaths(string assetRoot, ISet<string> paths, Action<string>? log)
    {
        var seedPath = Path.Combine(assetRoot, "static-assets.txt");
        if (!File.Exists(seedPath))
            return;

        foreach (var line in File.ReadLines(seedPath))
        {
            var value = line.Trim();
            if (value.Length == 0 || value.StartsWith('#'))
                continue;

            if (TryNormalizeStaticAssetPath(value, out var relative))
                paths.Add(relative);
            else
                log?.Invoke($"warning: 非法的 static-assets 种子行已跳过：{value}");
        }
    }

    /// <summary>把种子文件中的完整 URL 或带前缀路径规范化为 static-assets 下的相对路径。</summary>
    public static bool TryNormalizeStaticAssetPath(string value, out string relativePath)
    {
        relativePath = value.Trim().Replace('\\', '/');
        if (Uri.TryCreate(relativePath, UriKind.Absolute, out var uri))
            relativePath = Uri.UnescapeDataString(uri.AbsolutePath);

        relativePath = relativePath.TrimStart('/');
        const string productionPrefix = "production/static-assets/";
        const string shortPrefix = "static-assets/";
        if (relativePath.StartsWith(productionPrefix, StringComparison.OrdinalIgnoreCase))
            relativePath = relativePath[productionPrefix.Length..];
        else if (relativePath.StartsWith(shortPrefix, StringComparison.OrdinalIgnoreCase))
            relativePath = relativePath[shortPrefix.Length..];

        return AssetCatalogParser.IsSafeRelativePath(relativePath);
    }

    private static bool IsSafePathSegment(string value)
        => AssetCatalogParser.IsSafeRelativePath(value)
           && !value.Contains('/')
           && !value.Contains('\\');
}
