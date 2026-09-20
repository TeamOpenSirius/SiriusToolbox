using System.IO.Compression;
using System.Text.Json;

namespace Sirius.Toolbox.Assets;

/// <summary>
/// Unity Addressables catalog 解析器。逻辑与旧 Sirius.AssetTool 一致：
/// 展开 m_InternalIdPrefixes、还原 {AssetUrl} 等占位符、把逻辑资源键映射到可下载对象 URL。
/// Unity Addressables catalog parser. Behaviour matches the retired Sirius.AssetTool:
/// expand m_InternalIdPrefixes, substitute placeholders such as {AssetUrl}, and map
/// logical asset keys to downloadable object URLs.
/// </summary>
public static class AssetCatalogParser
{
    /// <summary>
    /// 生成 catalog 候选地址。非 Android 平台只尝试平台目录形态。
    /// Enumerate catalog candidates. Non-Android platforms only try the platform directory shape.
    /// </summary>
    public static IEnumerable<string> CatalogCandidates(
        string assetBase,
        string assetVersion,
        string category,
        string platform,
        string? catalogTemplate = null)
    {
        if (!string.IsNullOrWhiteSpace(catalogTemplate))
        {
            yield return catalogTemplate
                .Replace("{base}", assetBase, StringComparison.Ordinal)
                .Replace("{category}", category, StringComparison.Ordinal)
                .Replace("{version}", assetVersion, StringComparison.Ordinal)
                .Replace("{platform}", platform, StringComparison.OrdinalIgnoreCase);
            yield break;
        }

        // AnyProxy 导出的 curl 可能把 host 显示两次（https://host//host/production/...），
        // 那是代理重建产物；客户端实际请求的是平台目录形态。
        // AnyProxy exports can duplicate the host in the path; that is a proxy artifact.
        // The origin URL used by the client is the platform directory shape below.
        yield return $"{assetBase}/{category}/{platform}/{assetVersion}/catalog_{assetVersion}.json.br";
        yield return $"{assetBase}/{category}/{platform}/{assetVersion}/catalog_{assetVersion}.json";
        if (!string.Equals(platform, "Android", StringComparison.OrdinalIgnoreCase))
            yield break;

        var legacyCandidates = new[]
        {
            $"{assetBase}/{category}/catalog_{assetVersion}.json",
            $"{assetBase}/{category}/{assetVersion}/catalog_{assetVersion}.json",
            $"{assetBase}/{category}/{assetVersion}/catalog.json",
            $"{assetBase}/{category}/catalog.json",
            $"{assetBase}/{assetVersion}/{category}/catalog_{assetVersion}.json",
            $"{assetBase}/{assetVersion}/{category}/catalog.json",
            $"{assetBase}/production/{category}/catalog_{assetVersion}.json"
        };
        foreach (var candidate in legacyCandidates.Distinct(StringComparer.Ordinal))
            yield return candidate;
    }

    /// <summary>由 catalog 地址推导哈希文件地址。</summary>
    public static string GetCatalogHashUrl(string jsonUrl)
    {
        if (jsonUrl.EndsWith(".json.br", StringComparison.OrdinalIgnoreCase))
            return jsonUrl[..^8] + ".hash";
        if (jsonUrl.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return jsonUrl[..^5] + ".hash";
        return jsonUrl + ".hash";
    }

    /// <summary>
    /// catalog 可能是 Brotli 压缩的分块内容；仅在解压后确实是 JSON 时才采用解压结果。
    /// Catalogs may be Brotli encoded; the decoded bytes are only accepted when they are JSON.
    /// </summary>
    public static byte[] DecodeCatalogBytes(byte[] data)
    {
        if (data.Length == 0 || data[0] == (byte)'{')
            return data;

        try
        {
            using var input = new MemoryStream(data);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            brotli.CopyTo(output);
            var decoded = output.ToArray();
            return decoded.Length > 0 && decoded[0] == (byte)'{' ? decoded : data;
        }
        catch (InvalidDataException)
        {
            return data;
        }
    }

    /// <summary>
    /// 解析 catalog 中全部可下载对象。返回结果按 URL 去重。
    /// Parse every downloadable object from a catalog. Results are de-duplicated by URL.
    /// </summary>
    public static IReadOnlyList<CdnAssetObjectRecord> Parse(
        byte[] bytes,
        string catalogUrl,
        string category,
        string platform,
        string assetBase,
        string assetVersion)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var prefixes = root.TryGetProperty("m_InternalIdPrefixes", out var prefixNode)
                       && prefixNode.ValueKind == JsonValueKind.Array
            ? prefixNode.EnumerateArray()
                .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? string.Empty : string.Empty)
                .ToList()
            : [];

        var values = new List<string>();
        if (root.TryGetProperty("m_InternalIds", out var ids) && ids.ValueKind == JsonValueKind.Array)
        {
            values.AddRange(ids.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => ExpandInternalId(x.GetString()!, prefixes)));
        }

        if (values.Count == 0)
            CollectStrings(root, values);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var output = new Dictionary<string, CdnAssetObjectRecord>(StringComparer.Ordinal);
        foreach (var raw in values)
        {
            if (!TryResolveInternalId(
                    raw,
                    catalogUrl,
                    category,
                    platform,
                    assetBase,
                    assetVersion,
                    out var uri))
            {
                continue;
            }

            var url = uri.AbsoluteUri;
            if (output.ContainsKey(url))
                continue;

            output[url] = new CdnAssetObjectRecord
            {
                Url = url,
                Category = category,
                RelativePath = BuildRelativePath(uri, category),
                Status = CdnAssetStatus.Pending,
                UpdatedAtUnixMs = now
            };
        }

        return [.. output.Values];
    }

    /// <summary>
    /// 当 iOS catalog 与 Android 完全相同时，仍按平台池生成 iOS 对象地址：
    /// catalog 字节相同并不代表平台二进制相同。
    /// When the iOS catalog is identical to Android, iOS object URLs are still derived
    /// per platform because identical catalogs do not prove identical platform blobs.
    /// </summary>
    public static IReadOnlyList<CdnAssetObjectRecord> BuildPlatformEntries(
        IEnumerable<CdnAssetObjectRecord> sourceEntries,
        string targetPlatform)
    {
        const string sourcePlatform = "/Android/";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var output = new List<CdnAssetObjectRecord>();
        foreach (var source in sourceEntries)
        {
            var platformIndex = source.Url.IndexOf(sourcePlatform, StringComparison.OrdinalIgnoreCase);
            if (platformIndex < 0)
                continue;

            var targetUrl = string.Concat(
                source.Url.AsSpan(0, platformIndex + 1),
                targetPlatform,
                source.Url.AsSpan(platformIndex + sourcePlatform.Length - 1));
            output.Add(new CdnAssetObjectRecord
            {
                Url = targetUrl,
                RelativePath = BuildRelativePath(new Uri(targetUrl), source.Category),
                Category = source.Category,
                Status = CdnAssetStatus.Pending,
                UpdatedAtUnixMs = now
            });
        }

        return output;
    }

    /// <summary>给出对象在 assets/files 下的相对路径。</summary>
    public static string BuildRelativePath(Uri uri, string category)
    {
        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (string.IsNullOrWhiteSpace(path))
            path = "object";
        foreach (var character in Path.GetInvalidPathChars())
            path = path.Replace(character, '_');
        return $"{category}/{CdnAssetMirrorOptions.SanitizeFileName(uri.Host)}/{path}";
    }

    /// <summary>判断是否为可安全拼接的相对路径。</summary>
    public static bool IsSafeRelativePath(string path)
        => !string.IsNullOrWhiteSpace(path)
           && !Path.IsPathRooted(path)
           && !path.Contains("..", StringComparison.Ordinal)
           && !path.Contains('?')
           && !path.Contains('#');

    private static string ExpandInternalId(string value, IReadOnlyList<string> prefixes)
    {
        var separator = value.IndexOf('#');
        if (separator <= 0
            || !int.TryParse(value.AsSpan(0, separator), out var prefixIndex)
            || prefixIndex < 0
            || prefixIndex >= prefixes.Count)
        {
            return value;
        }

        return prefixes[prefixIndex] + value[(separator + 1)..];
    }

    private static bool TryResolveInternalId(
        string raw,
        string catalogUrl,
        string category,
        string platform,
        string assetBase,
        string assetVersion,
        out Uri uri)
    {
        uri = null!;
        var value = raw.Trim();
        if (value.Length == 0
            || value.StartsWith("jar:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = value.Replace("{AssetUrl}", assetBase, StringComparison.OrdinalIgnoreCase)
            .Replace("{AssetVersion}", assetVersion, StringComparison.OrdinalIgnoreCase)
            .Replace("{AssetType}", category, StringComparison.OrdinalIgnoreCase)
            .Replace("{AssetPlatform}", platform, StringComparison.OrdinalIgnoreCase);
        if (value.Contains("{UnityEngine.AddressableAssets.Addressables.RuntimePath}", StringComparison.Ordinal))
            return false;
        if (value.Contains('{') || value.Contains('}'))
            return false;
        if (value.StartsWith("//", StringComparison.Ordinal))
            value = "https:" + value;

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == "http" || absolute.Scheme == "https"))
        {
            if (CdnAssetMirrorOptions.DefaultCategories.Contains(absolute.Host, StringComparer.OrdinalIgnoreCase))
            {
                var logicalPath = absolute.AbsolutePath.TrimStart('/');
                var platformPrefix = platform + "/";
                if (!logicalPath.StartsWith(platformPrefix, StringComparison.OrdinalIgnoreCase))
                    return false;

                var contentPath = logicalPath[platformPrefix.Length..];
                uri = new Uri($"{assetBase}/{absolute.Host}/{platform}/{assetVersion}/{contentPath}{absolute.Query}");
                return true;
            }

            uri = absolute;
            return true;
        }

        // CRI Addressables catalog 里的相对 .acb/.awb/.usm 是逻辑资源键
        // （例如 Assets/AddressableAssets/BGM/...），可下载对象是生成的 .bundle。
        // Relative .acb/.awb/.usm values are logical asset keys; the downloadable
        // InternalIds are their generated .bundle objects.
        if (!value.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!Uri.TryCreate(new Uri(catalogUrl), value, out var relative)
            || (relative.Scheme != "http" && relative.Scheme != "https"))
        {
            return false;
        }

        uri = relative;
        return true;
    }

    private static void CollectStrings(JsonElement element, List<string> output)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                output.Add(element.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectStrings(item, output);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    CollectStrings(property.Value, output);
                break;
        }
    }
}
