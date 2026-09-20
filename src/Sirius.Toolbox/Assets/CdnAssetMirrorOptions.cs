namespace Sirius.Toolbox.Assets;

/// <summary>
/// CDN 资源镜像选项。默认分类、失败重试语义和旧 Sirius.AssetTool 保持一致。
/// CDN mirror options. Default categories and retry semantics match the retired Sirius.AssetTool.
/// </summary>
public sealed record CdnAssetMirrorOptions(string OutputDirectory)
{
    public const string StaticAssetsCategory = "static-assets";
    public const string NotationsCategory = "notations";
    public const string ScenesCategory = "scenes";

    public static readonly string[] DefaultCategories = ["2d-assets", "3d-assets", "cri-assets"];

    /// <summary>Addressables 分类；为空时使用 <see cref="DefaultCategories"/>。</summary>
    public IReadOnlyList<string> AssetCategories { get; init; } = [];

    /// <summary>自定义 catalog 模板，支持 {base}/{category}/{version}/{platform} 占位符。</summary>
    public string? CatalogTemplate { get; init; }

    /// <summary>只更新 catalog 与 manifest，不下载对象。</summary>
    public bool CatalogOnly { get; init; }

    /// <summary>忽略本地文件与状态，重新下载全部对象。</summary>
    public bool ForceAssets { get; init; }

    public int Concurrency { get; init; } = 12;
    public int Retries { get; init; } = 5;
    public int TimeoutMinutes { get; init; } = 10;
    public string UserAgent { get; init; } = "BestHTTP/2 v2.8.5";

    /// <summary>
    /// 直接把 OutputDirectory 作为 catalogs/files/manifests/indexes 的资源根目录。
    /// 默认关闭以兼容把 OutputDirectory 当作项目根目录的既有调用方。
    /// </summary>
    public bool UseOutputDirectoryAsAssetRoot { get; init; }

    /// <summary>跳过 static-assets（StaticContentUrl）发现与下载。</summary>
    public bool SkipStaticAssets { get; init; }

    /// <summary>
    /// 跳过场景资源。场景需要逐剧集调用官方 API，默认关闭；
    /// 显式改为 false 才会解析并下载 scenes。
    /// Skip episode scenes. Scenes need one authenticated API call per episode,
    /// so they stay disabled unless this is explicitly set to false.
    /// </summary>
    public bool SkipScenes { get; init; } = true;

    public bool InsecureTls { get; init; }

    public IReadOnlyList<string> ResolveCategories() =>
        AssetCategories.Count == 0 ? DefaultCategories : [.. AssetCategories];

    public string ManifestFileName(string assetVersion) =>
        $"cdn_{SanitizeFileName(assetVersion)}.json";

    internal static string SanitizeFileName(string value)
    {
        foreach (var character in Path.GetInvalidFileNameChars())
            value = value.Replace(character, '_');
        return value.Replace("..", "_", StringComparison.Ordinal);
    }
}

/// <summary>
/// 由官方环境与本地状态推导出的下载根地址与版本号。
/// Download roots and versions derived from the official environment and local state.
/// </summary>
public sealed record CdnAssetMirrorContext
{
    public required string AssetBaseUrl { get; init; }
    public required string AssetVersion { get; init; }
    public string StaticContentBaseUrl { get; init; } = string.Empty;
    public string MasterDataBaseUrl { get; init; } = string.Empty;
    public string MasterDataVersion { get; init; } = string.Empty;
}

/// <summary>
/// 单个剧集的官方详情查询结果。
/// Official episode detail projection used by scene discovery.
/// </summary>
public sealed record EpisodeSceneApiResult(string Title, string AssetSource);

/// <summary>
/// 官方剧集详情查询抽象，用于场景资源发现。
/// Official episode detail query abstraction used by scene asset discovery.
/// </summary>
public interface IEpisodeDetailApi
{
    Task<EpisodeSceneApiResult> GetEpisodeDetailAsync(long episodeMasterId, CancellationToken cancellationToken);
}
