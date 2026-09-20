namespace Sirius.Toolbox.Assets;

/// <summary>
/// 镜像对象状态。数值与既有 assets/manifests/cdn_*.json 保持兼容（Complete = 2）。
/// Mirror object status. Numeric values stay compatible with existing cdn_*.json manifests (Complete = 2).
/// </summary>
public enum CdnAssetStatus
{
    Pending = 0,
    Downloading = 1,
    Complete = 2,
    Failed = 3,
    NotFound = 4
}

/// <summary>
/// CDN 镜像对象记录，字段与旧 Sirius.AssetTool 的 AssetObjectRecord 一一对应。
/// A mirrored CDN object. Field names mirror the retired Sirius.AssetTool AssetObjectRecord.
/// </summary>
public sealed class CdnAssetObjectRecord
{
    public string Url { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public long ExpectedSize { get; set; }
    public long DownloadedSize { get; set; }
    public string? ETag { get; set; }
    public string? LastModified { get; set; }
    public string? ContentMd5 { get; set; }
    public string? Sha256 { get; set; }
    public string? AliasOfUrl { get; set; }
    public CdnAssetStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public long UpdatedAtUnixMs { get; set; }
}

/// <summary>
/// Addressables 目录记录。保留旧记录形状，既有 manifest 与增量 diff 逻辑可直接复用。
/// An Addressables catalog record. Kept in the legacy shape so existing manifests stay reusable.
/// </summary>
public sealed record CdnAssetCatalogRecord(
    string Category,
    string JsonUrl,
    string HashUrl,
    string LocalJson,
    string LocalHash,
    string Hash,
    string Platform = "Android");

/// <summary>
/// CDN 镜像清单（assets/manifests/cdn_&lt;assetVersion&gt;.json）。
/// The CDN mirror manifest stored at assets/manifests/cdn_&lt;assetVersion&gt;.json.
/// </summary>
public sealed class CdnAssetManifest
{
    public int Version { get; set; } = 3;
    public string AssetBase { get; set; } = string.Empty;
    public string AssetVersion { get; set; } = string.Empty;
    public long UpdatedAtUnixMs { get; set; }
    public List<CdnAssetCatalogRecord> Catalogs { get; set; } = [];
    public Dictionary<string, CdnAssetObjectRecord> Objects { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 剧集主数据索引条目，用于向官方 API 请求单个剧集的场景资源地址。
/// Episode master entry used to request a single episode scene asset location.
/// </summary>
public sealed record CdnEpisodeMasterIndexRecord(long Id, string Title);

/// <summary>
/// 场景来源索引（assets/indexes/episodes_&lt;masterDataVersion&gt;.json）。
/// Scene source index persisted at assets/indexes/episodes_&lt;masterDataVersion&gt;.json.
/// </summary>
public sealed class CdnEpisodeIndexManifest
{
    public int Version { get; set; } = 1;
    public string MasterDataVersion { get; set; } = string.Empty;
    public long UpdatedAtUnixMs { get; set; }
    public Dictionary<long, CdnEpisodeIndexRecord> Episodes { get; set; } = [];
}

/// <summary>
/// 单个剧集的场景来源记录。
/// Scene asset source for a single episode.
/// </summary>
public sealed class CdnEpisodeIndexRecord
{
    public long EpisodeMasterId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? AssetSource { get; set; }
    public string? Error { get; set; }
    public long UpdatedAtUnixMs { get; set; }
}

/// <summary>
/// 远端 catalog 哈希探测结果。
/// Result of probing the remote catalog hash.
/// </summary>
public readonly record struct RemoteCatalogHash(string JsonUrl, string HashUrl, byte[] Bytes, string Hash);

/// <summary>
/// CDN 镜像阶段。
/// CDN mirror stages.
/// </summary>
public enum CdnAssetMirrorStage
{
    Catalog,
    Notations,
    StaticAssets,
    Scenes,
    Download,
    Manifest,
    Complete
}

/// <summary>
/// CDN 镜像进度消息。
/// Progress message emitted by the CDN asset mirror.
/// </summary>
public sealed record CdnAssetMirrorProgress(
    CdnAssetMirrorStage Stage,
    string Message,
    int Completed = 0,
    int Total = 0);

/// <summary>
/// CDN 镜像结果汇总。
/// Summary of a completed CDN mirror run.
/// </summary>
public sealed record CdnAssetMirrorResult(
    string ManifestPath,
    string AssetVersion,
    int ObjectCount,
    int DownloadedCount,
    int NotFoundCount,
    int RemovedCount,
    int CatalogCount,
    bool CatalogOnly);
