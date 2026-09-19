# Validation

This revision unifies the MasterData editor, Chart tools, Episode tools, scene-assets cache builder, Episode editor, and combined MasterData/CDN/R2 synchronizer in the `Sirius.ToolboxUI` WinForms application. Chart, Episode, cache, editor, and R2 operations are exposed through reusable `Sirius.AssetTool` services so the GUI and CLI share the same processing paths.

## ToolboxUI behavior

- the start window provides separate MasterData, Chart, Episode, 剧情资源缓存, 剧情编辑器, and 主数据 / CDN / R2 全量同步 entries;
- opening a child hides the start window, and closing the child restores and activates it;
- repeated activation reuses the existing child window;
- all visible tool-window text is Chinese;
- MasterData keeps its temporary working-copy, JSON editing, validation, save, and save-as behavior;
- Episode supports single-file pack/unpack, directory batch pack, directory batch BIN-to-JSON unpack, and inspection.
- the cache service covers Version 2 output, nested Chinese paths, SHA-256/metadata-only modes, unmatched JSON/BIN mapping, and duplicate-ID rejection;
- the Episode editor preserves unknown wrapper metadata, readable Chinese JSON, common-field edits, record mutations, and verified BIN export.
- the R2 MasterData uploader validates `master/manifest.json` and `master/mastermemory.db`, maps the manifest URI, supports dry-run/force/retry modes, and skips matching remote SHA-256 objects.
- the full R2 synchronizer discovers catalogs and CDN files, writes the complete dry-run mapping, reuses/persists `r2-hash-cache.json`, seeds hashes from CDN manifests, supports concurrency/retries/force, and skips matching remote SHA-256 objects.

## Automated checks

- the solution project paths and project references were updated for `Sirius.ToolboxUI`;
- the service harness uses temporary paths containing spaces and Chinese characters and covers Chart conversion plus Episode pack/unpack, batch pack, batch unpack, inspect, cache generation/loading, duplicate detection, metadata-only output, and editor JSON/BIN round-trips;
- `dotnet build .\SiriusTools.sln` completes with zero warnings and zero errors;
- the Chart self-test and Episode help command complete successfully;
- `scripts\smoke-test.ps1` exercises the non-network CLI help/self-test paths and confirms the ToolboxUI Release executable exists.
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
