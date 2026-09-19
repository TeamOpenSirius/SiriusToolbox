# SiriusToolbox full project contents

This archive contains the complete Toolbox project, including the original chart and episode tooling plus MasterMemory CLI and WinForms editing.

- `src/Sirius.AssetTool/Charts`: SUS parsing/conversion and Sirius chart ENC encode/decode.
- `src/Sirius.AssetTool/R2`: reusable Cloudflare R2 MasterData object mapping, SHA-256 comparison, SigV4 signing, retry, and upload service.
- `src/Sirius.AssetTool/Episodes`: episode pack/unpack commands, cache generation, editor persistence, and binary diagnostics.
- `src/Sirius.Tooling.Core/Episodes`: shared episode models/codecs.
- `src/Sirius.MasterData`: shared typed MasterMemory table/record CRUD and rebuild implementation.
- `src/Sirius.MasterTool`: MasterData synchronization plus typed MasterMemory CLI.
- `src/Sirius.ToolboxUI`: WinForms start center with MasterData, Chart, Episode, cache, Episode editor, and combined 主数据 / CDN / R2 全量同步 tools.
- `scripts/build-release.ps1`: produces the self-contained compressed single-file ToolboxUI distribution binary.
- `scripts/clean.ps1`: removes generated build/publish directories and the migrated empty GUI directory.
- `lib/Sirius.Protocol.dll`: generated protocol/MasterMemory models used by `Sirius.MasterData`.

MasterMemory CLI supports table/schema/list/get/add/update/delete/verify/export-json plus localization text workflows.
ToolboxUI supports separate MasterData, Chart, Episode, scene-cache, Episode editor, and one combined 主数据 / CDN / R2 全量同步 window. MasterData supports table filtering, paged record browsing, primary-key lookup, schema inspection, JSON record editing, add/duplicate/delete, verification, and save/save-as. Episode supports single-file packing/unpacking plus directory batch packing and BIN-to-JSON batch unpacking. The cache window builds and browses Version 2 `scene-assets.json`; the editor preserves wrapper metadata and exports edited BIN files. The combined synchronizer previews or publishes MasterData, catalogs, and CDN files, with mapping output, hash-cache reuse, remote SHA-256 skipping, concurrency, retries, and force mode.
