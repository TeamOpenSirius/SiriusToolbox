# Sirius Toolbox

## English

Sirius Toolbox is the desktop tool suite for World Dai Star assets and MasterData.
`Sirius.Toolbox` contains reusable implementations; `Sirius.ToolboxUI` is the only
application entry point. There is no AssetTool or MasterTool command-line executable.

### Capabilities

- convert SUS charts and encode/decode Sirius chart files;
- pack, unpack, inspect, and edit Episode scene files;
- build Version 2 `scene-assets.json` files with independent Episode JSON and scene BIN CDN prefixes;
- download the official `mastermemory.db`, verify it through generated MasterMemory models, and export typed JSON;
- mirror the complete official asset set incrementally: Addressables catalogs, catalog objects, static-assets, notations, and episode scenes, with `.part` resume, hash-cache reuse, and per-run added/unchanged/removed reporting;
- browse, add, update, duplicate, and delete MasterMemory records with validated atomic saves;
- preview and publish MasterData, catalogs, scenes, notations, and other CDN files to Cloudflare R2;
- use concurrent upload, retry, dry-run, remote SHA-256 checks, local hash reuse, custom directory mappings, and “only upload local changes” mode.

Shared MasterMemory models and editing code come from the sibling
`E:\Ymst\Projects\SiriusData` repository. Models are not copied into this repository.

### Package dependencies

Toolbox consumes `Sirius.Protocol` and `Sirius.MasterData` from the TeamOpenSirius
GitHub Packages feed. The repository `nuget.config` contains the feed and local
sibling package fallback. GitHub Actions authenticates with `GITHUB_TOKEN`; local
builds need a GitHub token with `read:packages` when the sibling package cache is
not available.

### Build and run

```powershell
dotnet build .\SiriusTools.sln -c Release
dotnet run --project .\src\Sirius.ToolboxUI\Sirius.ToolboxUI.csproj
```

The start window provides separate tools for MasterData editing, official MasterData
download, charts, Episodes, scene-index generation, Episode editing, and R2 publication.

The R2 window accepts either the output root or its `assets` directory. Custom mappings
use `local-directory=object-prefix`, for example
`scenes-zh-cn=master-data/production/scenes-zh-cn`. Custom mappings override defaults.
The first “only upload local changes” run verifies remote objects; later runs avoid HEAD
requests for unchanged local files.

See [architecture](docs/architecture.md) and [ToolboxUI usage](docs/toolboxui.md).
See [official CDN mirror](docs/asset/cdn-mirror.md) for the asset-side sync rules.

## 中文

Sirius Toolbox 是 World Dai Star 的桌面资源与主数据工具箱。
`Sirius.Toolbox` 子项目承载可复用实现，`Sirius.ToolboxUI` 是唯一应用入口；
不再提供 AssetTool 或 MasterTool 命令行可执行程序。

主要功能包括谱面转换、Episode 场景处理、Version 2 场景索引、官方 MasterMemory
下载/校验/导出、官方 CDN 资源增量镜像（catalog、catalog 对象、static-assets、
notations、剧集场景，支持 `.part` 续传与新增/未变/移除统计）、主数据编辑，以及
MasterData/CDN/R2 预览和发布。R2 支持并发、重试、远端 SHA-256、本地哈希缓存、
自定义目录映射和“仅上传本地变更”。

MasterMemory 模型与编辑实现来自同级 `E:\Ymst\Projects\SiriusData`，本仓库不复制模型。

```powershell
dotnet build .\SiriusTools.sln -c Release
dotnet run --project .\src\Sirius.ToolboxUI\Sirius.ToolboxUI.csproj
```

R2 窗口可选择输出根目录或其中的 `assets` 目录。自定义映射格式为
`本地目录=对象前缀`，例如 `scenes-zh-cn=master-data/production/scenes-zh-cn`；
自定义映射优先于默认规则。“仅上传本地变更”首次会核对远端，之后未变化文件不再发送 HEAD。

官方资源同步规则详见 [CDN 镜像](docs/asset/cdn-mirror.md)。
