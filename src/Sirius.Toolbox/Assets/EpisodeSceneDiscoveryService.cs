using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Sirius.Toolbox.IO;

namespace Sirius.Toolbox.Assets;

/// <summary>
/// 剧集场景发现选项。
/// Options for episode scene discovery.
/// </summary>
public sealed record EpisodeSceneDiscoveryOptions
{
    public required string RootDirectory { get; init; }
    public string? AssetDirectory { get; init; }
    public required string MasterDataVersion { get; init; }
    public string MasterDataBaseUrl { get; init; } = string.Empty;
    public int Concurrency { get; init; } = 12;
    public int Retries { get; init; } = 5;
    public Action<string>? Log { get; init; }
}

/// <summary>
/// 剧集场景发现结果。
/// Result of an episode scene discovery pass.
/// </summary>
public sealed record EpisodeSceneDiscoveryResult(
    IReadOnlyList<CdnAssetObjectRecord> Objects,
    int EpisodeCount,
    int ResolvedCount,
    int FailureCount);

/// <summary>
/// 通过官方剧集详情接口解析每个剧集的场景资源地址，并缓存到
/// assets/indexes/episodes_&lt;masterDataVersion&gt;.json。
/// Resolves each episode scene asset location through the official episode details endpoint and
/// caches the result at assets/indexes/episodes_&lt;masterDataVersion&gt;.json.
/// </summary>
public sealed class EpisodeSceneDiscoveryService
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public string GetIndexPath(
        string rootDirectory,
        string masterDataVersion,
        string? assetDirectory = null) =>
        Path.Combine(
            string.IsNullOrWhiteSpace(assetDirectory)
                ? Path.Combine(Path.GetFullPath(rootDirectory), "assets")
                : Path.GetFullPath(assetDirectory),
            "indexes",
            $"episodes_{CdnAssetMirrorOptions.SanitizeFileName(masterDataVersion)}.json");

    public async Task<EpisodeSceneDiscoveryResult> DiscoverAsync(
        EpisodeSceneDiscoveryOptions options,
        IEpisodeDetailApi api,
        Func<string, bool> shouldRefreshSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(shouldRefreshSource);

        var root = Path.GetFullPath(options.RootDirectory);
        var masterJsonDirectory = Path.Combine(root, "master", "json");
        var episodeMasterPath = Path.Combine(masterJsonDirectory, "EpisodeMaster.json");
        if (!File.Exists(episodeMasterPath))
        {
            options.Log?.Invoke("warning: EpisodeMaster.json 不可用；场景发现已跳过。");
            return new EpisodeSceneDiscoveryResult([], 0, 0, 0);
        }

        var episodes = ReadEpisodeMasters(masterJsonDirectory);
        var indexPath = GetIndexPath(root, options.MasterDataVersion, options.AssetDirectory);
        var index = await LoadIndexAsync(indexPath, cancellationToken);
        if (!string.Equals(index.MasterDataVersion, options.MasterDataVersion, StringComparison.Ordinal))
            index = new CdnEpisodeIndexManifest { MasterDataVersion = options.MasterDataVersion };

        var pending = episodes
            .Where(episode => !index.Episodes.TryGetValue(episode.Id, out var cached)
                              || string.IsNullOrWhiteSpace(cached.AssetSource)
                              || !IsSourceFresh(cached.AssetSource)
                              || shouldRefreshSource(cached.AssetSource))
            .ToList();
        options.Log?.Invoke($"剧集场景索引：正在解析 {pending.Count}/{episodes.Count} 个剧集…");

        var failures = new ConcurrentBag<long>();
        var started = 0;
        await Parallel.ForEachAsync(
            pending,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, options.Concurrency),
                CancellationToken = cancellationToken
            },
            async (episode, token) =>
            {
                var itemNumber = Interlocked.Increment(ref started);
                options.Log?.Invoke($"[场景索引 {itemNumber}/{pending.Count}] episode={episode.Id} title={episode.Title}");
                try
                {
                    var result = await GetEpisodeDetailWithRetryAsync(api, episode.Id, options.Retries, token);
                    if (string.IsNullOrWhiteSpace(result.AssetSource))
                    {
                        throw new InvalidDataException(
                            $"剧集接口没有返回 EpisodeDetailAssetSource：episode={episode.Id}。");
                    }

                    await SaveIndexEntryAsync(indexPath, index, new CdnEpisodeIndexRecord
                    {
                        EpisodeMasterId = episode.Id,
                        Title = string.IsNullOrWhiteSpace(result.Title) ? episode.Title : result.Title,
                        AssetSource = result.AssetSource,
                        UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }, token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failures.Add(episode.Id);
                    await SaveIndexEntryAsync(indexPath, index, new CdnEpisodeIndexRecord
                    {
                        EpisodeMasterId = episode.Id,
                        Title = episode.Title,
                        Error = exception.Message,
                        UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }, token);
                    options.Log?.Invoke($"[场景索引失败] episode={episode.Id} error={exception.Message}");
                }
            });

        var resolved = 0;
        var objects = new List<CdnAssetObjectRecord>();
        foreach (var episode in episodes)
        {
            if (!index.Episodes.TryGetValue(episode.Id, out var record)
                || string.IsNullOrWhiteSpace(record.AssetSource)
                || !TryResolveSceneSource(record.AssetSource, options.MasterDataBaseUrl, out var uri))
            {
                continue;
            }

            var fileName = Path.GetFileName(uri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = $"{episode.Id}.bin";
            resolved++;
            objects.Add(new CdnAssetObjectRecord
            {
                Url = uri.AbsoluteUri,
                RelativePath = $"{CdnAssetMirrorOptions.ScenesCategory}/{CdnAssetMirrorOptions.SanitizeFileName(fileName)}",
                Category = CdnAssetMirrorOptions.ScenesCategory,
                Status = CdnAssetStatus.Pending,
                UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }

        return new EpisodeSceneDiscoveryResult(objects, episodes.Count, resolved, failures.Count);
    }

    /// <summary>
    /// 解析场景来源：优先使用绝对 URL，否则相对 MasterData 根地址拼接。
    /// Resolves a scene source: absolute URLs win, otherwise the path is relative to the MasterData root.
    /// </summary>
    public static bool TryResolveSceneSource(string source, string masterDataBaseUrl, out Uri uri)
    {
        uri = null!;
        if (Uri.TryCreate(source, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            uri = absolute;
            return true;
        }

        if (string.IsNullOrWhiteSpace(masterDataBaseUrl)
            || !Uri.TryCreate(masterDataBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var masterBase)
            || !Uri.TryCreate(masterBase, source.TrimStart('/'), out var relative)
            || (relative.Scheme != Uri.UriSchemeHttp && relative.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        uri = relative;
        return true;
    }

    /// <summary>
    /// SAS 来源在到期前一小时内视为过期，需要重新解析。
    /// A SAS source is considered stale inside the last hour before expiry and must be resolved again.
    /// </summary>
    public static bool IsSourceFresh(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return false;

        var expiryPart = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(part => part.StartsWith("se=", StringComparison.OrdinalIgnoreCase));
        if (expiryPart is null)
            return true;

        var encoded = expiryPart[(expiryPart.IndexOf('=') + 1)..];
        return DateTimeOffset.TryParse(Uri.UnescapeDataString(encoded), out var expiry)
               && expiry > DateTimeOffset.UtcNow.AddHours(1);
    }

    private static async Task<EpisodeSceneApiResult> GetEpisodeDetailWithRetryAsync(
        IEpisodeDetailApi api,
        long episodeMasterId,
        int retries,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await api.GetEpisodeDetailAsync(episodeMasterId, cancellationToken);
            }
            catch (HttpRequestException exception) when (attempt < retries
                                                         && (exception.StatusCode == HttpStatusCode.TooManyRequests
                                                             || (int?)exception.StatusCode >= 500))
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt + 1))), cancellationToken);
            }
        }
    }

    private static List<CdnEpisodeMasterIndexRecord> ReadEpisodeMasters(string masterJsonDirectory)
    {
        var output = new Dictionary<long, CdnEpisodeMasterIndexRecord>();
        AddEpisodeIdsFromFile(output, Path.Combine(masterJsonDirectory, "EpisodeMaster.json"), "Id", 0, "Title", 2);
        AddEpisodeIdsFromFile(output, Path.Combine(masterJsonDirectory, "StoryEventEpisodeMaster.json"), "Id", 0, "Title", 4);
        AddEpisodeIdsFromFile(output, Path.Combine(masterJsonDirectory, "SpecialEpisodeMaster.json"), "Id", 0, "Title", 2);
        AddEpisodeIdsFromFile(output, Path.Combine(masterJsonDirectory, "CharacterEpisodeMaster.json"), "EpisodeMasterId", 2, null, null);
        AddEpisodeIdsFromFile(output, Path.Combine(masterJsonDirectory, "SpotConversationMaster.json"), "EpisodeMasterId", 9, "Title", 15);
        AddEpisodeIdsFromFile(output, Path.Combine(masterJsonDirectory, "TheaterChapterMaster.json"), "EpisodeMasterId", 6, "Name", 1);
        return [.. output.Values.OrderBy(record => record.Id)];
    }

    private static void AddEpisodeIdsFromFile(
        Dictionary<long, CdnEpisodeMasterIndexRecord> output,
        string path,
        string idPropertyName,
        int idIndex,
        string? titlePropertyName,
        int? titleIndex)
    {
        if (!File.Exists(path))
            return;

        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return;

        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (!JsonProbe.TryGetPropertyOrArrayElement(row, idPropertyName, idIndex, out var idNode)
                || idNode.ValueKind == JsonValueKind.Null
                || !idNode.TryGetInt64(out var id)
                || id <= 0)
            {
                continue;
            }

            var title = string.Empty;
            if (titlePropertyName is not null
                && titleIndex is not null
                && JsonProbe.TryGetPropertyOrArrayElement(row, titlePropertyName, titleIndex.Value, out var titleNode)
                && titleNode.ValueKind == JsonValueKind.String)
            {
                title = titleNode.GetString() ?? string.Empty;
            }

            if (!output.TryGetValue(id, out var existing) || string.IsNullOrWhiteSpace(existing.Title))
                output[id] = new CdnEpisodeMasterIndexRecord(id, title);
        }
    }

    private static async Task<CdnEpisodeIndexManifest> LoadIndexAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return new CdnEpisodeIndexManifest();

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<CdnEpisodeIndexManifest>(stream, cancellationToken: cancellationToken)
                   ?? new CdnEpisodeIndexManifest();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new CdnEpisodeIndexManifest();
        }
    }

    private async Task SaveIndexEntryAsync(
        string path,
        CdnEpisodeIndexManifest index,
        CdnEpisodeIndexRecord entry,
        CancellationToken cancellationToken)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            index.Episodes[entry.EpisodeMasterId] = entry;
            index.UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            AtomicFile.WriteAllText(
                path,
                JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
