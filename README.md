# SiriusTools

SiriusTools is a .NET toolkit for working with World Dai Star MasterData and local game assets. The repository contains two CLI tools, a unified WinForms toolbox, and shared libraries.

- **Sirius.MasterTool** retrieves the current MasterData manifest, downloads `mastermemory.db`, exports typed tables, and can inspect/add/update/delete MasterMemory records from the CLI.
- **Sirius.ToolboxUI** is a WinForms toolbox with separate MasterData, Chart, Episode, scene-cache, Episode-editor, and one combined MasterData/CDN/R2-sync window.
- **Sirius.AssetTool** converts SUS charts to the Sirius chart format, encodes and decodes chart ENC files, packs or unpacks episode scene binaries, and builds `scene-assets.json` caches.
- **Sirius.MasterData** contains the shared typed MasterMemory read/write implementation used by both the CLI and GUI.
- **Sirius.Tooling.Core** contains shared protocol contracts, MessagePack codecs, episode codecs, persistence helpers, and file utilities.

## Requirements

- .NET 10 SDK
- Network access for `Sirius.MasterTool`

Check the installed SDK with:

```powershell
dotnet --info
```

## Build

Build the complete solution:

```powershell
dotnet restore .\SiriusTools.sln
dotnet build .\SiriusTools.sln -c Release
```

Create a distributable single-file Windows binary:

```powershell
.\scripts\build-release.ps1
```

The default output is a self-contained, compressed `artifacts\ToolboxUI-win-x64\Sirius.ToolboxUI.exe`. Use `-FrameworkDependent` for a smaller binary that requires the .NET 10 Desktop Runtime, or `-ReadyToRun` when startup speed is preferred over file size. Clean generated `bin`, `obj`, and `artifacts` directories with:

```powershell
.\scripts\clean.ps1
```

A repository smoke test is also available:

```powershell
.\scripts\smoke-test.ps1
```

The script restores and builds the solution, then runs the non-network help commands for both tools.

## Quick start

### Open the ToolboxUI

```powershell
dotnet run --project .\src\Sirius.ToolboxUI\Sirius.ToolboxUI.csproj -- .\mastermemory.db
```

The start window opens separate MasterData, Chart, Episode, 剧情资源缓存, 剧情编辑器, and 主数据 / CDN / R2 全量同步 tools. The combined sync tool includes MasterData automatically, along with catalogs and CDN resources. The MasterData tool supports table browsing, paging, primary-key lookup, schema inspection, JSON record editing, add/duplicate/delete, verification, and safe save/save-as. See [ToolboxUI documentation](docs/toolboxui.md).

### Sync MasterData and CDN assets to Cloudflare R2

The combined ToolboxUI 主数据 / CDN / R2 全量同步 window and `r2 sync` command upload the `master/mastermemory.db` selected by `master/manifest.json` together with catalogs and mirrored CDN files. The default mode in the GUI is preview-only.

Preview the object mapping without credentials or network access:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- r2 sync .\output --dry-run
```

For an actual upload, set the R2 API token credentials in the environment and use the endpoint/bucket options when they differ from the defaults:

```powershell
$env:R2_ACCESS_KEY_ID = '<access-key-id>'
$env:R2_SECRET_ACCESS_KEY = '<secret-access-key>'
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- r2 sync .\output --endpoint 'https://<account-id>.r2.cloudflarestorage.com' --bucket '<bucket>'
```

The implementation uses Cloudflare R2's S3-compatible API and SigV4 signing. See the [Cloudflare R2 S3 API](https://developers.cloudflare.com/r2/api/s3/api/) documentation for endpoint and credential details.

For the complete CDN publication flow, use the full sync command. It discovers MasterData, catalogs, and mirrored files, writes `assets/r2-object-map.tsv` in preview mode, and maintains `assets/r2-hash-cache.json` during real uploads:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- r2 sync .\output --dry-run --prefix release
```

The equivalent GUI is the “主数据 / CDN / R2 全量同步” window. It includes MasterData in the same mapping scan and upload operation, plus concurrency, retries, force mode, dry-run, remote SHA-256 skipping, hash-cache reuse, and progress logs.


### Synchronize MasterData

```powershell
dotnet run --project .\src\Sirius.MasterTool\Sirius.MasterTool.csproj -- sync --dir output
```

The default output contains the downloaded MasterMemory database, local manifests and state, and per-table JSON when JSON export is enabled.

See [MasterTool documentation](docs/master/README.md).
### Edit MasterMemory records

```powershell
dotnet run --project .\src\Sirius.MasterTool\Sirius.MasterTool.csproj -- db tables .\output\master\mastermemory.db
dotnet run --project .\src\Sirius.MasterTool\Sirius.MasterTool.csproj -- db update .\output\master\mastermemory.db MusicMaster --key 1001 --set Name="Updated" -o .\mastermemory.modified.db
```

See [MasterTool usage](docs/master/usage.md) for list/get/add/update/delete and localization commands.


### Convert a SUS chart

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- chart text chart.sus chart.txt
```

Create an ENC file:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- chart encode chart.sus chart.enc --key "<32-byte-key>"
```

### Pack an episode scene

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- episode pack episode.json -o scene.bin
```

Batch-pack a JSON directory, or reverse a BIN directory back to JSON:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- episode pack-dir .\episode-json -o .\episode-bin
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- episode unpack-dir .\episode-bin -o .\episode-json-restored
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- episode cache .\episode-json .\episode-bin -o .\scene-assets.json --metadata-only
```

The ToolboxUI also provides a Chinese Episode editor for common dialogue/resource fields and can export the edited document back to BIN. See [AssetTool documentation](docs/asset/README.md) and [ToolboxUI documentation](docs/toolboxui.md).

## Repository layout

```text
SiriusTools.sln
Directory.Build.props
Directory.Packages.props

src/
  Sirius.Tooling.Core/
    Episodes/              Episode protocol models and codecs
    IO/                    Shared file utilities
    Master/                MasterData protocols, export, persistence, serialization

  Sirius.MasterData/
    MasterMemoryDatabaseService.cs  Shared typed MasterMemory CRUD
    MasterMemoryBinary.cs           Table-block rebuild support

  Sirius.MasterTool/
    Cli/                   Command-line parsing
    Configuration/         Synchronization options
    Networking/            Official API client and resumable download logic

  Sirius.ToolboxUI/
    ToolboxHomeForm.cs     Chinese start center and child-window lifecycle
    MasterToolForm.cs      MasterData editor window
    ChartToolForm.cs       Chart conversion/ENC window
    EpisodeToolForm.cs     Episode pack/unpack window
    SceneAssetCacheForm.cs scene-assets.json cache builder/viewer
    EpisodeEditorForm.cs   simple Episode JSON editor and BIN exporter
    R2SyncForm.cs           combined MasterData/CDN/R2 sync window

  Sirius.AssetTool/
    Charts/                SUS parsing, conversion, chart format and ENC codec
    Episodes/              Episode commands, cache builder, editor service and binary diagnostics

docs/
  master/                  MasterTool reference
  asset/                   AssetTool reference
```

## Project dependencies

```text
Sirius.MasterTool ───> Sirius.MasterData ───> Sirius.Tooling.Core
Sirius.ToolboxUI  ───> Sirius.MasterData ───> Sirius.Tooling.Core
       │             └──────────────────────> Sirius.AssetTool (including R2 MasterData upload)
Sirius.AssetTool  ─────────────────────────> Sirius.Tooling.Core
```

`Sirius.Tooling.Core` stays independent of the applications. `Sirius.MasterData` owns the generated MasterMemory dependency, and both MasterTool and ToolboxUI consume that shared layer. ToolboxUI also consumes the reusable Chart/Episode services exposed by AssetTool.

## Build configuration

Common project settings are defined in `Directory.Build.props`:

- target framework: `net10.0`;
- nullable reference types enabled;
- implicit global usings enabled;
- warnings treated as errors;
- Release optimization, server GC, tiered compilation, and tiered PGO enabled.

NuGet package versions are managed centrally through `Directory.Packages.props`. `Sirius.MasterData` references `lib/Sirius.Protocol.dll` plus `MasterMemory`, `MessagePack`, and `MagicOnion.Abstractions`; `Sirius.Tooling.Core` remains independent of the generated MasterMemory model assembly. `Sirius.ToolboxUI` overrides the common target to `net10.0-windows` and enables WinForms.

MasterMemory table schemas are provided by the generated models and metadata in `lib/Sirius.Protocol.dll`; there is no duplicate Toolbox schema file.

## Architecture

For project boundaries and data flows, see [Repository Architecture](docs/architecture.md).
