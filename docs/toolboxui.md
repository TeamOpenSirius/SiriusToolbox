# ToolboxUI

## English

`Sirius.ToolboxUI` is the only supported entry point. Its start center opens one
instance of each tool window:

- MasterData editor: table/schema browsing, paging, primary-key lookup, JSON editing,
  add/duplicate/delete, verification, Save and Save As, and the offline repack window;
- offline MasterMemory repack: export typed JSON, edit the working copy, then rebuild
  `mastermemory.db` against an unmodified baseline; tables that match the baseline or
  decode to the same values keep their original payload block, so untouched tables are
  byte-for-byte identical and only edited tables are re-encoded;
- official MasterData sync: authentication, resumable download, generated-model
  verification, manifest/state/publication output, optional typed JSON export, and
  incremental CDN asset mirroring;
- Chart and Episode tools;
- Episode editor and Version 2 scene-index builder;
- combined MasterData/CDN/R2 publication.

The scene-index window has independent CDN prefixes for Episode JSON `SourcePath` and
scene BIN `RelativePath`. Metadata-only mode does not read BIN contents; normal mode
computes SHA-256. Duplicate numeric IDs are rejected before output is replaced.

The R2 window defaults to dry-run. It supports concurrency, retries, force upload,
remote length/SHA-256 checks, DPAPI-protected saved credentials, custom mappings, and
an “only upload local changes” baseline. A mapping such as
`scenes-zh-cn=master-data/production/scenes-zh-cn` maps that directory below
`assets/files` to the specified object prefix.

## Official MasterData / CDN sync window

The window has two tabs. **账号与主数据** holds the API bootstrap URL, client version,
registration name, platform, GameVersion, FM, login/access tokens, the output directory,
and the toggles for forcing a MasterData re-download, exporting typed JSON, skipping
MasterData, skipping CDN assets, and ignoring TLS errors. **CDN 资源** holds the asset
categories (default `2d-assets,3d-assets,cri-assets`), an optional catalog template,
concurrency, retries, timeout, User-Agent, and the toggles for skipping static-assets,
skipping episode scenes, updating only catalogs plus the manifest, and forcing a full
asset re-download.

Both tabs run through one 开始同步 action; 取消 stops the current run. The log pane
streams the sync stage and the asset mirror's per-file progress. The result summary
reports the MasterData version, verification row/table counts and SHA-256, and the asset
object/download/404/removed/catalog counts. See
[official CDN mirror](asset/cdn-mirror.md) for the asset-side rules.

## Offline repack window

Open it from the home screen or from the MasterData editor's `数据库 → 离线重打包...`
menu. Fill in the source database, the working JSON directory, an optional baseline
JSON directory, and the output database, then run `1. 导出 JSON` followed by
`2. 重打包数据库`. Keep `严格字节校验` enabled for the strict round trip: after a
no-op export the output must hash identically to the source. The log reports the table
count, which tables were rebuilt, which kept their original blocks, and both SHA-256
values. The end-to-end workflow is also described in the server repository's
`docs/masterdata-tool.md`.

## 中文

`Sirius.ToolboxUI` 是唯一支持的入口。首页分别打开主数据编辑器、官方 MasterData
同步、谱面、剧情、剧情编辑器、Version 2 场景索引和 MasterData/CDN/R2 发布窗口。

场景索引窗口可分别配置剧情 JSON 的 `SourcePath` 与场景 BIN 的 `RelativePath` CDN
前缀；仅元数据模式不读取 BIN，普通模式计算 SHA-256，重复数字 ID 会在写入前拒绝。

R2 窗口默认仅预览，支持并发、重试、强制上传、远端长度/SHA-256 检查、DPAPI
保护的凭据、自定义目录映射和“仅上传本地变更”。

## 官方 MasterData / CDN 同步窗口

窗口分两个页签。**账号与主数据** 页包含 API 地址、客户端版本、注册名、平台、
GameVersion、FM、登录/访问令牌、输出目录，以及强制重下主数据、导出类型化 JSON、
跳过 MasterData、跳过 CDN 资源、忽略 TLS 证书错误等开关。**CDN 资源** 页包含资源分类
（默认 `2d-assets,3d-assets,cri-assets`）、可选 catalog 模板、并发、重试、超时、
User-Agent，以及跳过 static-assets、跳过剧集场景、仅更新 catalog 与清单、强制重下
全部资源对象等开关。

CDN 产物直接写入所选输出目录的 `catalogs`、`files`、`manifests` 与 `indexes`，不会再额外
创建一层 `assets` 子目录。MasterData 仍位于同一输出目录的 `master` 子目录。

两个页签共用一次“开始同步”，“取消”会停止当前运行。日志区按阶段输出同步进度和
镜像的逐文件进度；结束时会汇总 MasterData 版本、校验表/行数与 SHA-256，以及资源的
对象数、下载数、404 数、移除数和 catalog 数。资源侧规则详见
[官方 CDN 镜像](asset/cdn-mirror.md)。

## 离线重打包窗口

可从首页，或主数据编辑器的「数据库 → 离线重打包...」菜单打开。填入源数据库、
工作 JSON 目录、可选的 baseline JSON 目录与输出数据库，然后依次执行
「1. 导出 JSON」与「2. 重打包数据库」。严格回包时保持「严格字节校验」开启：未改动
的导出重打包后必须与源数据库哈希一致。日志会输出表数量、哪些表被重建、哪些表保留
原始块，以及两个 SHA-256。
