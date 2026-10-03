# Sirius Toolbox project contents

`Sirius.Toolbox` holds every reusable implementation; `Sirius.ToolboxUI` is the only
executable entry point. There is no AssetTool or MasterTool command-line executable.

- `src/Sirius.Toolbox/Charts`: SUS parsing/conversion and Sirius chart ENC encode/decode.
- `src/Sirius.Toolbox/Episodes`: episode pack/unpack, Version 2 scene index, editor
  persistence, and binary diagnostics.
- `src/Sirius.Toolbox/Assets`: Unity Addressables catalog parsing, static-asset and
  notation discovery, episode scene discovery, and the incremental CDN mirror.
- `src/Sirius.Toolbox/R2`: Cloudflare R2 object mapping, SHA-256 comparison, SigV4
  signing, retry, hash cache, and the MasterData/CDN publication service.
- `src/Sirius.Toolbox/Master`: official registration/authentication, `mastermemory.db`
  download, verification, publication manifests, and the full sync service.
- `src/Sirius.Toolbox/IO`: atomic file helpers shared by the asset and master services.
- `src/Sirius.ToolboxUI`: WinForms start center plus the MasterData editor, official
  MasterData/CDN sync, Chart, Episode, scene cache, Episode editor, and
  MasterData/CDN/R2 publication windows.
- `tests/Sirius.Toolbox.Tests`: non-network service harness covering Chart, Episode,
  Addressables catalog parsing, CDN mirror resume/diffing, and R2 planning.
- `scripts/build-release.ps1`: produces the self-contained compressed single-file
  ToolboxUI distribution binary.
- `scripts/smoke-test.ps1`: restores, builds, runs the service harness and the
  `--self-test-ui` harness, and checks the Release executable.
- `Directory.Build.targets`: downloads and validates public SiriusData rolling
  Release packages into per-project obj feeds/caches before restore.

Shared protocol models and typed MasterMemory editing live in the independent
SiriusData repository. This repository consumes their rolling packages and does
not require a sibling checkout or a fallback DLL. See docs/rolling-packages.md.

## Capability summary

ToolboxUI exposes separate MasterData, Chart, Episode, scene-cache, Episode editor,
official MasterData/CDN sync, and MasterData/CDN/R2 publication windows. MasterData
supports table filtering, paged browsing, primary-key lookup, schema inspection, JSON
editing, add/duplicate/delete, verification, and save/save-as. Episode supports
single-file packing/unpacking plus directory batch packing and batch unpacking. The
cache window builds and browses Version 2 `scene-assets.json`; the editor preserves
wrapper metadata and exports edited BIN files. The official sync window downloads and
verifies `mastermemory.db` and mirrors catalogs, static assets, notations, and episode
scenes incrementally. The publication window previews or uploads MasterData, catalogs,
and CDN files with mapping output, hash-cache reuse, remote SHA-256 skipping,
concurrency, retries, and force mode.
