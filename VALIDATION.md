# Validation

This revision unifies the MasterData editor, official MasterData/CDN download, Chart tools, Episode tools, scene-assets cache builder, Episode editor, and MasterData/CDN/R2 publication in the `Sirius.ToolboxUI` WinForms application. Every operation is implemented by the reusable `Sirius.Toolbox` class library; `Sirius.ToolboxUI` is the only entry point and the repository ships no AssetTool or MasterTool command-line executable.

## ToolboxUI behavior

- the start window provides separate MasterData, Chart, Episode, 剧情资源缓存, 剧情编辑器, 主数据 / CDN / R2 全量同步, and 官方 MasterData / CDN 同步 entries;
- opening a child hides the start window, and closing the child restores and activates it;
- repeated activation reuses the existing child window;
- all visible tool-window text is Chinese;
- MasterData keeps its temporary working-copy, JSON editing, validation, save, and save-as behavior;
- Episode supports single-file pack/unpack, directory batch pack, directory batch BIN-to-JSON unpack, and inspection.
- the cache service covers Version 2 output, nested Chinese paths, SHA-256/metadata-only modes, unmatched JSON/BIN mapping, and duplicate-ID rejection;
- the Episode editor preserves unknown wrapper metadata, readable Chinese JSON, common-field edits, record mutations, and verified BIN export.
- the R2 MasterData uploader validates `master/manifest.json` and `master/mastermemory.db`, maps the manifest URI, supports dry-run/force/retry modes, and skips matching remote SHA-256 objects.
- the full R2 synchronizer discovers catalogs and CDN files, writes the complete dry-run mapping, reuses/persists `r2-hash-cache.json`, seeds hashes from CDN manifests, supports concurrency/retries/force, and skips matching remote SHA-256 objects.
- the official sync window registers/authenticates, downloads and verifies `mastermemory.db`, optionally exports typed JSON, and mirrors the complete CDN asset set incrementally, including catalogs, static assets, notations, and episode scenes.

## Automated checks

- the solution project paths and project references were updated for `Sirius.ToolboxUI`;
- the service harness uses temporary paths containing spaces and Chinese characters and covers Chart conversion plus Episode pack/unpack, batch pack, batch unpack, inspect, cache generation/loading, duplicate detection, metadata-only output, and editor JSON/BIN round-trips;
- the service harness also covers Addressables catalog parsing (Brotli decoding, `m_InternalIdPrefixes` expansion, iOS platform re-derivation), CDN mirror incremental diffing (added/unchanged/removed), `.part` resume with `Range`/206, `416` promotion of a complete partial file, and `404` recording;
- `dotnet build .\SiriusTools.sln` completes with zero warnings and zero errors;
- `dotnet run --project .\tests\Sirius.Toolbox.Tests -c Release` completes successfully;
- `scripts\smoke-test.ps1` restores, builds, runs the service harness and the UI self-test, and confirms the ToolboxUI Release executable exists.
- the smoke script also runs `Sirius.ToolboxUI --self-test-ui`, constructing the home form and all seven tool windows without opening a visible window to catch layout construction regressions.
- `scripts\build-release.ps1` publishes a self-contained compressed single-file `Sirius.ToolboxUI.exe` under `artifacts\ToolboxUI-win-x64`.

## Manual startup check

Run the following on Windows with the .NET 10 SDK. It enters the WinForms message loop without requiring a database:

```powershell
dotnet run --project .\src\Sirius.ToolboxUI\Sirius.ToolboxUI.csproj
```

To open the MasterData child with a database at startup, pass its path after `--`:

```powershell
dotnet run --project .\src\Sirius.ToolboxUI\Sirius.ToolboxUI.csproj -- .\mastermemory.db
```
