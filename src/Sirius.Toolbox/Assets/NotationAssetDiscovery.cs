using System.Globalization;
using System.Text.Json;
using Sirius.Protocol.Shared;

namespace Sirius.Toolbox.Assets;

/// <summary>
/// notations 发现选项。
/// Options for notation discovery.
/// </summary>
public sealed record NotationDiscoveryOptions
{
    public required string RootDirectory { get; init; }
    public required string AssetBaseUrl { get; init; }
    public Action<string>? Log { get; init; }
}

/// <summary>
/// 从 LiveMaster / AnotherNotationMaster / DugongRunCourseMaster 推导 notations 下载清单。
/// 规则与旧 Sirius.AssetTool 的 DiscoverNotationObjects 一致，包含上架时间过滤与 music_config.enc。
/// Derives the notation download set from the music master tables. The rules match the retired
/// Sirius.AssetTool DiscoverNotationObjects implementation, including availability filtering
/// and the per-directory music_config.enc entry.
/// </summary>
public static class NotationAssetDiscovery
{
    public const string Category = CdnAssetMirrorOptions.NotationsCategory;

    public static IReadOnlyList<CdnAssetObjectRecord> Discover(NotationDiscoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = Path.GetFullPath(options.RootDirectory);
        var masterJsonDirectory = Path.Combine(root, "master", "json");
        var liveMasterPath = Path.Combine(masterJsonDirectory, "LiveMaster.json");
        if (!File.Exists(liveMasterPath))
        {
            options.Log?.Invoke("warning: LiveMaster.json 不可用；notations 发现已跳过。");
            return [];
        }

        var assetBase = options.AssetBaseUrl.TrimEnd('/');
        var now = DateTimeOffset.UtcNow;
        var notationDirectories = new HashSet<string>(StringComparer.Ordinal);
        var notationPaths = new HashSet<string>(StringComparer.Ordinal);

        AddNotationPaths<MusicDifficulties>(
            liveMasterPath,
            "MusicMasterId",
            notationIdIndex: 2,
            difficultyIndex: 1,
            notationDirectories,
            notationPaths,
            now,
            startDateProperty: "StartDate",
            startDateIndex: 7,
            endDateProperty: "EndDate",
            endDateIndex: 8,
            options.Log);
        AddNotationPaths<MusicDifficulties>(
            Path.Combine(masterJsonDirectory, "AnotherNotationMaster.json"),
            "NotationPath",
            notationIdIndex: 3,
            difficultyIndex: 4,
            notationDirectories,
            notationPaths,
            now,
            startDateProperty: "StartDate",
            startDateIndex: 7,
            endDateProperty: "EndDate",
            endDateIndex: 8,
            options.Log);
        AddNotationPaths<DugongRunDifficultyTypes>(
            Path.Combine(masterJsonDirectory, "DugongRunCourseMaster.json"),
            "NotationPath",
            notationIdIndex: 4,
            difficultyIndex: 2,
            notationDirectories,
            notationPaths,
            now,
            startDateProperty: null,
            startDateIndex: -1,
            endDateProperty: null,
            endDateIndex: -1,
            options.Log);

        foreach (var directory in notationDirectories)
            notationPaths.Add($"{directory}/music_config.enc");

        var nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return [.. notationPaths
            .Where(AssetCatalogParser.IsSafeRelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new CdnAssetObjectRecord
            {
                Url = $"{assetBase}/Notations/{path}",
                RelativePath = $"{Category}/{path}",
                Category = Category,
                Status = CdnAssetStatus.Pending,
                UpdatedAtUnixMs = nowUnixMs
            })];
    }

    private static void AddNotationPaths<TDifficulty>(
        string path,
        string notationIdProperty,
        int notationIdIndex,
        int difficultyIndex,
        HashSet<string> notationDirectories,
        HashSet<string> output,
        DateTimeOffset availableAt,
        string? startDateProperty,
        int startDateIndex,
        string? endDateProperty,
        int endDateIndex,
        Action<string>? log)
        where TDifficulty : struct, Enum
    {
        if (!File.Exists(path))
            return;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return;

            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (!JsonProbe.IsAvailableAt(
                        row,
                        availableAt,
                        startDateProperty,
                        startDateIndex,
                        endDateProperty,
                        endDateIndex)
                    || !TryGetNotationFields(
                        row,
                        notationIdProperty,
                        notationIdIndex,
                        difficultyIndex,
                        out var notationId,
                        out var difficultyNode)
                    || !TryReadPathSegment(notationId, out var notationPath)
                    || !TryReadDifficulty<TDifficulty>(difficultyNode, out var difficulty))
                {
                    continue;
                }

                notationDirectories.Add(notationPath);
                output.Add($"{notationPath}/{difficulty}.enc");
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            log?.Invoke($"warning: {Path.GetFileName(path)} notation 发现失败：{exception.Message}");
        }
    }

    private static bool TryGetNotationFields(
        JsonElement row,
        string notationIdProperty,
        int notationIdIndex,
        int difficultyIndex,
        out JsonElement notationId,
        out JsonElement difficulty)
    {
        notationId = default;
        difficulty = default;

        if (row.ValueKind == JsonValueKind.Object)
            return row.TryGetProperty(notationIdProperty, out notationId)
                   && row.TryGetProperty("Difficulty", out difficulty);

        if (row.ValueKind != JsonValueKind.Array
            || notationIdIndex < 0
            || difficultyIndex < 0
            || row.GetArrayLength() <= Math.Max(notationIdIndex, difficultyIndex))
        {
            return false;
        }

        notationId = row[notationIdIndex];
        difficulty = row[difficultyIndex];
        return true;
    }

    private static bool TryReadPathSegment(JsonElement value, out string path)
    {
        path = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
        return AssetCatalogParser.IsSafeRelativePath(path) && !path.Contains('/');
    }

    private static bool TryReadDifficulty<TDifficulty>(JsonElement value, out int difficulty)
        where TDifficulty : struct, Enum
    {
        difficulty = 0;
        if (value.ValueKind == JsonValueKind.Number)
            return value.TryGetInt32(out difficulty) && difficulty > 0;

        if (value.ValueKind != JsonValueKind.String)
            return false;

        var text = value.GetString();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out difficulty))
            return difficulty > 0;
        if (!Enum.TryParse<TDifficulty>(text, true, out var parsed))
            return false;

        difficulty = Convert.ToInt32(parsed, CultureInfo.InvariantCulture);
        return difficulty > 0;
    }
}
