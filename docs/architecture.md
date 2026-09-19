# Repository Architecture

SiriusTools is organized as one .NET solution with two CLI applications, one WinForms application, and two shared libraries.

## Dependency graph

```text
Sirius.MasterTool ───┐
                     ├──> Sirius.MasterData ───> Sirius.Tooling.Core
Sirius.ToolboxUI  ────┘             │
                                   └──> lib/Sirius.Protocol.dll

Sirius.AssetTool ─────────────────────> Sirius.Tooling.Core
Sirius.ToolboxUI ────────────────────> Sirius.AssetTool
                                      └── R2 MasterData/CDN sync (S3/SigV4)
```

The MasterMemory dependency is intentionally isolated in `Sirius.MasterData`. Chart and episode tooling does not pull in WinForms or the generated MasterMemory model assembly.

## Sirius.Tooling.Core

Reusable non-UI infrastructure:

- Master API MessagePack contracts and serialization;
- synchronization state/publication persistence;
- episode MessagePack wire contracts and codecs;
- atomic file replacement helpers.

It does not reference any executable/UI project or `Sirius.Protocol.dll`.

## Sirius.MasterData

Shared typed MasterMemory data layer used by both MasterTool and ToolboxUI.

It owns:

- loading tables through `Sirius.Protocol.Shared.MemoryDatabase`;
- generated metadata/schema discovery;
- table/record listing and primary-key lookup;
- add/update/delete;
- changed-table-only MasterMemory binary rebuild;
- full database re-load and generated validation after writes;
- JSON export and localization CSV workflows.

`Sirius.MasterData` is the only Toolbox project that directly references the supplied generated `lib/Sirius.Protocol.dll` and MasterMemory runtime packages.

## Sirius.MasterTool

Owns network synchronization and CLI presentation. MasterMemory CLI commands delegate to `Sirius.MasterData`.

Synchronization flow:

```text
CLI options
  -> fetch Environment
  -> resolve API endpoint and asset version
  -> load/reuse/register account credentials
  -> authenticate/login
  -> fetch /api/data/master
  -> download/resume mastermemory.db when required
  -> write local manifest/state
  -> optionally export typed JSON
  -> write publication.json
```

## Sirius.ToolboxUI

Chinese Windows Forms toolbox with a start center and separate MasterData, Chart, Episode, scene-cache, Episode-editor, and one combined MasterData/CDN/R2-sync child window.

```text
mastermemory.db
  -> temporary working copy
  -> Sirius.MasterData table/schema/record API
  -> paged DataGridView + JSON record editor
  -> add/update/delete on working copy
  -> full validation
  -> Save / Save As (+ .bak when overwriting)
```

The UI never implements its own binary serializer or duplicate DTOs. CLI and GUI therefore use the same conversion, key parsing, rebuild, validation, chart, and episode service behavior.

The combined sync window and `Sirius.AssetTool r2 sync` command share `R2AssetSyncService`, including MasterData, the old catalog/CDN discovery rules, `r2-object-map.tsv`, `r2-hash-cache.json`, concurrent uploads, retries, and remote SHA-256 skipping. The legacy `r2 masterdata` CLI remains available for scripts, while both CLI paths delegate signed HEAD/PUT requests to the S3-compatible client in AssetTool.

## Sirius.AssetTool

Entirely local chart/episode tooling.

### Chart pipeline

```text
SUS file
  -> SusParser
  -> TempoMap
  -> SusToSiriusConverter
  -> Sirius plaintext rows
  -> Brotli compression
  -> AES-256-CBC
  -> ENC file
```

### Episode pipeline

```text
JSON wrapper or EpisodeDetail[]
  -> validation
  -> EpisodeDetail[]
  -> MessagePack + LZ4BlockArray
  -> scene BIN
```

The cache/editor pipeline stays in the reusable Episode service layer:

```text
Episode JSON directory + scene BIN directory
  -> SceneAssetCacheService
  -> Version 2 scene-assets.json

Episode JSON wrapper/array
  -> EpisodeEditorService (preserve wrapper metadata)
  -> readable JSON or EpisodeCodec
  -> verified scene BIN
```

## Boundary rules

1. Official API synchronization policy stays in `Sirius.MasterTool`; the explicitly local R2 publication command stays with its reusable AssetTool service.
2. WinForms presentation stays in `Sirius.ToolboxUI`.
3. Typed MasterMemory operations shared by CLI/UI stay in `Sirius.MasterData`.
4. SUS/chart-specific conversion stays in `Sirius.AssetTool`.
5. Episode command presentation, cache discovery, editor persistence, and file diagnostics stay in `Sirius.AssetTool`; reusable wire codecs stay in `Sirius.Tooling.Core`.
6. `Sirius.Tooling.Core` must not reference applications or generated MasterMemory models.
7. `Sirius.AssetTool` must not reference MasterTool, ToolboxUI, or MasterData.
