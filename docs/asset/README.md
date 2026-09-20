# Asset workflows

Chart, Episode, scene-index, and CDN/R2 implementations live in `Sirius.Toolbox` and
are exposed through dedicated windows in `Sirius.ToolboxUI`. There is no AssetTool CLI.

The Chart window handles SUS conversion and ENC encode/decode. Episode windows handle
pack/unpack/inspection, editing, and Version 2 scene-index generation. The R2 window
publishes MasterData, catalogs, scenes, notations, and custom directory mappings.
The official MasterData / CDN sync window downloads the official asset set
incrementally; see [official CDN mirror](cdn-mirror.md).

## Document map

| Document | Scope |
| --- | --- |
| [cdn-mirror.md](cdn-mirror.md) | Official Addressables catalog, static-asset, notation, and episode-scene mirroring, including resume and incremental diff behaviour |
| [chart-validation.md](chart-validation.md) | Chart conversion validation expectations |

# 资源工作流

谱面、剧情、场景索引和 CDN/R2 实现均位于 `Sirius.Toolbox`，通过
`Sirius.ToolboxUI` 的独立窗口提供，不再存在 AssetTool CLI。

谱面窗口负责 SUS 转换与 ENC 编解码；剧情窗口负责打包/解包/检查、编辑和
Version 2 场景索引；R2 窗口发布 MasterData、catalog、场景、notations 及自定义目录映射；
官方 MasterData / CDN 同步窗口负责增量下载官方资源集，详见
[官方 CDN 镜像](cdn-mirror.md)。

| 文档 | 范围 |
| --- | --- |
| [cdn-mirror.md](cdn-mirror.md) | 官方 Addressables catalog、static-assets、notations 与剧集场景镜像，含续传与增量差异行为 |
| [chart-validation.md](chart-validation.md) | 谱面转换的校验要求 |
