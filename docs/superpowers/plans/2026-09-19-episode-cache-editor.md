# Episode 资源缓存与简易剧情编辑器实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 `Sirius.ToolboxUI` 增加 `scene-assets.json` Version 2 缓存生成/查看能力和可保存、可打包的简易剧情编辑器。

**Architecture:** 在 `Sirius.AssetTool.Episodes` 增加 `SceneAssetCacheService` 与 `EpisodeEditorService`，让 CLI、GUI 和测试共用文件扫描、JSON 读写、哈希、Episode 编解码逻辑。ToolboxUI 保留现有三类工具入口，并新增独立的剧情资源缓存窗口和剧情编辑器窗口；窗口只负责参数、异步执行和中文呈现。

**Tech Stack:** .NET 10, C#, WinForms, `System.Text.Json.Nodes`, existing `EpisodeCodec`, `AtomicFile`, MessagePack/LZ4 episode protocol.

**Spec:** `docs/superpowers/specs/2026-09-19-episode-cache-editor-design.md`

## Global Constraints

- GUI project remains `net10.0-windows` with WinForms.
- GUI-visible text is Chinese; `scene-assets.json` field names and CLI switches remain unchanged.
- Existing Episode single-file and batch pack/unpack behavior must not regress.
- Cache generation is local and does not require network access or Git executable access.
- Cache output and editor saves refuse existing destinations unless the user explicitly enables overwrite.
- JSON is UTF-8, indented, and keeps Chinese characters readable instead of escaping them as `\\uXXXX`.
- Existing dirty-worktree changes are preserved; no reset, checkout, broad deletion, or unrelated refactor.

## Review Focus

- Episode JSON wrapper with unknown top-level fields: editor save must preserve them.
- JSON and BIN files in nested directories with Chinese names and spaces: relative paths must remain stable and use `/`.
- BIN without matching JSON and JSON without matching BIN: cache should report both cases without silently dropping a discovered BIN.
- Duplicate numeric BIN IDs: cache generation must fail before writing a partial output.
- Existing cache/editor output files: default refusal and explicit overwrite path must both be tested.
- Empty EpisodeDetail arrays and records with character motions: editor load/save/pack must preserve valid protocol data.

---

### Task 1: Add failing cache/editor service cases to the regression harness

**Files:**
- Modify: `tests/Sirius.AssetTool.Tests/Program.cs`
- Modify: `tests/Sirius.AssetTool.Tests/Sirius.AssetTool.Tests.csproj` only if a reference is needed

**Interfaces:**
- The harness will consume `SceneAssetCacheService`, `SceneAssetCacheOptions`, `EpisodeEditorService`, and `EpisodeEditorDocument` from `Sirius.AssetTool.Episodes`.

- [x] **Step 1: Add cache fixture assertions before the service exists**

Create temporary directories named with Chinese characters and spaces: `episode-json`, `scenes-bin`, and a nested subdirectory. Write one wrapper JSON with `EpisodeId`, `Title`, an extra unknown property, and one `EpisodeDetail`; create its BIN with the existing codec through the current Episode service. Add assertions for a Version 2 cache, matched source path, SHA-256, nested relative path, and an unmatched BIN.

- [x] **Step 2: Add metadata-only and duplicate-ID cases**

Add a second build using `MetadataOnly: true` and assert empty hash/algorithm and zero timestamp. Add two BIN files with the same numeric stem in different subdirectories and assert the build throws before producing the requested output.

- [x] **Step 3: Add editor load/save/pack assertions**

Load a wrapper containing an unknown top-level property, modify the first phrase, append/duplicate a detail, save to a new JSON file, reload it, and assert the unknown property plus all requested details remain. Pack the edited document, unpack it, and assert the changed phrase and character-motion array survive.

- [x] **Step 4: Run the harness and verify the expected red failure**

Run:

```powershell
dotnet run --project .\tests\Sirius.AssetTool.Tests\Sirius.AssetTool.Tests.csproj
```

Expected: compilation fails because the new service/result types do not yet exist. If the harness fails for a fixture or path reason instead, correct the test before writing production code.

### Task 2: Implement `SceneAssetCacheService`

**Files:**
- Create: `src/Sirius.AssetTool/Episodes/SceneAssetCacheService.cs`
- Modify: `src/Sirius.AssetTool/Episodes/EpisodeCommands.cs` only when the CLI adapter is added in Task 5

**Interfaces:**

Create these public types in `Sirius.AssetTool.Episodes`:

```csharp
public sealed record SceneAssetCacheOptions(
    bool MetadataOnly,
    string? MasterDataVersion,
    string? SourceRevision,
    string EpisodePathPrefix = "episode",
    string ScenePathPrefix = "scenes");

public sealed record SceneAssetCacheEntry(
    long EpisodeMasterId,
    string RelativePath,
    string FileName,
    string Sha256,
    string GitObjectId,
    string HashAlgorithm,
    string SourcePath,
    bool MetadataOnly,
    long FileSize,
    long LastWriteTimeUtcTicks);

public sealed record SceneAssetCacheDocument(
    int Version,
    string MasterDataVersion,
    string SourceRevision,
    DateTimeOffset GeneratedAt,
    IReadOnlyDictionary<string, SceneAssetCacheEntry> Assets);

public sealed record SceneAssetCacheBuildResult(
    string OutputPath,
    int AssetCount,
    int MatchedJsonCount,
    int MissingJsonCount,
    long TotalBytes);

public sealed class SceneAssetCacheService
{
    public SceneAssetCacheBuildResult Build(
        string episodeDirectory,
        string sceneDirectory,
        string outputPath,
        SceneAssetCacheOptions options,
        bool overwrite);

    public SceneAssetCacheDocument Load(string cachePath);
}
```

- [x] **Step 1: Implement path and ID discovery**

Validate both directories, recursively enumerate `*.json` and `*.bin`, normalize relative paths to `/`, derive JSON IDs from `EpisodeCodec.ReadJson` with a numeric filename fallback, and derive BIN IDs from numeric file stems. Reject duplicate JSON/BIN IDs with useful paths.

- [x] **Step 2: Implement entry construction and Version 2 serialization**

For every BIN create one entry keyed by the decimal ID. Match `SourcePath` by ID when available. Compute SHA-256 and UTC ticks unless `MetadataOnly` is enabled. Leave `GitObjectId` empty, set `HashAlgorithm` to `sha256` only when a hash is present, and serialize `Version = 2` with `GeneratedAt = DateTimeOffset.UtcNow` using readable indented UTF-8 JSON.

- [x] **Step 3: Implement atomic output, overwrite protection, and loading**

Reject existing output unless `overwrite` is true, write with `AtomicFile.WriteAllText`, and make `Load` validate the Version 2 shape and return a typed document. Do not leave an output file when validation fails.

- [x] **Step 4: Run the service harness to turn the cache cases green**

Run the harness and confirm cache content, metadata-only behavior, unmatched BIN reporting, duplicate rejection, and load all pass before moving to editor implementation.

### Task 3: Implement `EpisodeEditorService` with metadata preservation

**Files:**
- Create: `src/Sirius.AssetTool/Episodes/EpisodeEditorService.cs`
- Modify: `src/Sirius.Tooling.Core/Episodes/EpisodeCodec.cs` only if a narrowly scoped shared write helper is required

**Interfaces:**

```csharp
public sealed class EpisodeEditorDocument
{
    public bool IsWrapper { get; }
    public List<EpisodeDetailResult> Details { get; }
}

public sealed record EpisodeEditorSaveResult(string OutputPath, int DetailCount);
public sealed record EpisodeEditorPackResult(string OutputPath, int DetailCount, long ByteCount);

public sealed class EpisodeEditorService
{
    public EpisodeEditorDocument Load(string inputPath);
    public EpisodeEditorSaveResult Save(EpisodeEditorDocument document, string outputPath, bool overwrite);
    public EpisodeEditorPackResult Pack(EpisodeEditorDocument document, string outputPath, bool overwrite);
}
```

- [x] **Step 1: Parse and validate while retaining the original wrapper node**

Load the raw JSON as `JsonNode`, call `EpisodeCodec.ReadJson` for protocol validation, retain an internal wrapper `JsonObject` when the root is an object, and expose a mutable detail list. Array-root JSON remains array-root JSON.

- [x] **Step 2: Implement readable save and protocol-safe pack**

On save, replace only the `EpisodeDetail` node in the retained wrapper or replace the array root, preserving unknown wrapper properties. Use UTF-8 without BOM, indentation, and `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`. On pack, call `EpisodeCodec.Pack`, `EpisodeCodec.Verify`, and `AtomicFile.WriteAllBytes` after overwrite validation.

- [x] **Step 3: Run editor harness cases**

Run the service harness and confirm unknown wrapper metadata, Chinese text, detail list changes, empty arrays, character motions, and BIN round-trip all pass.

### Task 4: Add the cache and editor GUI windows

**Files:**
- Create: `src/Sirius.ToolboxUI/SceneAssetCacheForm.cs`
- Create: `src/Sirius.ToolboxUI/EpisodeEditorForm.cs`
- Modify: `src/Sirius.ToolboxUI/ToolboxHomeForm.cs`
- Modify: `src/Sirius.ToolboxUI/ToolboxWindowRegistry.cs` only if a lifecycle helper needs a small extension

**Interfaces:**
- `SceneAssetCacheForm` consumes `SceneAssetCacheService` and exposes build/view controls.
- `EpisodeEditorForm` consumes `EpisodeEditorService` and exposes a mutable `EpisodeEditorDocument`.
- `ToolboxHomeForm` adds keys `episode-cache` and `episode-editor`, reuses one instance per key, hides home on open, and restores home on child close.

- [x] **Step 1: Build the Chinese cache window**

Add JSON directory, BIN directory, output cache path, MasterData/source revision fields, metadata-only and overwrite controls, a build button, a cache-open button, summary labels, and an asset `DataGridView` with ID/path/source/size/hash columns. Run file work via `Task.Run`, disable controls while busy, and render errors in Chinese.

- [x] **Step 2: Build the editor document navigation**

Add open/save/save-as/pack buttons, a left record list showing order/speaker/phrase, and a right editor for the specified common fields. Populate controls from the selected `EpisodeDetailResult`, keep advanced fields in the object, and use a suppress-selection flag plus a Yes/No/Cancel prompt for unapplied changes.

- [x] **Step 3: Add record mutation actions**

Implement new, duplicate, delete, apply, and reload actions. New/duplicate records must retain a valid `EpisodeMasterId` and initialize `CharacterMotions` to an empty array. Mark the document dirty after mutations and update the list summary after apply.

- [x] **Step 4: Add save and BIN packing flows**

Save to the current path or show Save As, call the editor service, keep the window open on failure, and show record/byte counts on success. Pack the current document to a selected BIN path using the same overwrite protection and round-trip verification as the existing Episode tool.

- [x] **Step 5: Add both windows to the home launcher**

Change the launcher button layout to accommodate the existing three tools plus “剧情资源缓存” and “剧情编辑器”. Register both forms with the existing owner/close callback lifecycle and keep all visible labels Chinese.

- [x] **Step 6: Build the UI project**

Run:

```powershell
dotnet build .\src\Sirius.ToolboxUI\Sirius.ToolboxUI.csproj
```

Expected: zero warnings and zero errors.

### Task 5: Add the cache CLI adapter

**Files:**
- Modify: `src/Sirius.AssetTool/Episodes/EpisodeCommands.cs`
- Modify: `docs/asset/README.md`
- Modify: `README.md`

**Interfaces:**
- Add `episode cache <episode目录> <scene目录> [-o output] [--metadata-only] [--master-data-version value] [--source-revision value] [--force]`.

- [x] **Step 1: Add command parsing and help text**

Project arguments to `SceneAssetCacheService`, preserve Chinese error output and return nonzero for invalid input or service failures.

- [x] **Step 2: Add success summary and documentation examples**

Print output path, asset count, matched/missing JSON counts, total bytes, and document both metadata and full-hash modes.

- [x] **Step 3: Run CLI help and a temporary cache command**

Run Episode help and the harness fixture through the CLI adapter; expect exit 0 for valid input and a documented nonzero result for duplicate IDs.

### Task 6: Integrate documentation, regression checks, and final verification

**Files:**
- Modify: `scripts/smoke-test.ps1`
- Modify: `PROJECT_CONTENTS.md`
- Modify: `VALIDATION.md`
- Modify: `docs/architecture.md`
- Modify: `docs/toolboxui.md`

- [x] **Step 1: Document the new windows and cache workflow**

Document the two new launcher entries, expected `episode`/`scenes` directory layout, cache output fields, editor save/pack behavior, and CLI command.

- [x] **Step 2: Extend smoke coverage without launching a GUI**

Keep CLI help/self-test headless, run the service harness, and assert the ToolboxUI build output exists. Do not require a visible form for the smoke script.

- [x] **Step 3: Run the complete verification set**

Run:

```powershell
dotnet build .\SiriusTools.sln
dotnet build .\SiriusTools.sln -c Release
dotnet run --project .\tests\Sirius.AssetTool.Tests\Sirius.AssetTool.Tests.csproj
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- chart selftest
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- episode --help
.\scripts\smoke-test.ps1
```

Expected: all commands exit 0, builds have zero warnings/errors, and the service harness reports cache/editor plus existing Chart/Episode cases passed.

- [x] **Step 4: Inspect final status and diff**

Run `git status --short`, `git diff --check`, verify no old GUI directory or test process remains, and preserve unrelated pre-existing worktree changes.
