# 自制 Master 编辑器与官方同步移除 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 在现有 MasterData 运营编辑器中加入从空白创建卡面、活动、音乐 Master 的原子工作流，并彻底移除官方 MasterData/CDN 下载链路。

**Architecture:** 新增独立的创建批次服务，基于生成模型 schema 构造 JSON 记录，在临时数据库中按批次调用 `MasterMemoryDatabaseService.AddRecord` 并最终 `Verify`。UI 以共享动态字段编辑器承载三个业务向导；官方同步删除只影响官方 API/认证/下载路径，R2/CDN 本地发布保持独立。

**Tech Stack:** C# / .NET 10 / WinForms / `Sirius.MasterData` / xUnit（现有测试框架）。

**Spec:** `docs/superpowers/specs/2026-10-02-custom-master-editor-design.md`

## Global Constraints

- `Sirius.MasterData.MasterMemoryDatabaseService` 是唯一的 MasterMemory 读写和校验入口。
- 新记录必须在临时数据库中完成整批写入，并通过 `Verify` 后才替换工作副本。
- 二进制资源只编辑 Master 中的 Key/路径，不在 Toolbox 生成图片、音频或 Banner 文件。
- 保留现有运营编辑器、通用表/JSON 编辑器、离线重打包和 R2/CDN 本地发布。
- 最终保存继续生成 `.bak`。

## Review Focus

- 空数据库或高度不足的数据库创建批次不应产生非法 ID 或崩溃；由 Task 2 的主键分配测试覆盖。
- 批次中任意一条记录失败时，原工作副本必须保持不变；由 Task 2 的回滚测试覆盖。
- 外键指向本批次新记录时必须绑定正确；由 Task 3 的活动/音乐批次测试覆盖。
- 资源路径不存在时应在确认前阻止提交，明确跳过后才能继续；由 Task 3 的资源校验测试覆盖。
- 官方同步删除后首页自检不能引用已删类型，同时 R2 入口仍可构造；由 Task 1 的启动 smoke test 覆盖。

### Task 1: 移除官方 MasterData/CDN 同步链路

**Files:**
- Modify: `src/Sirius.ToolboxUI/ToolboxHomeForm.cs`
- Modify: `src/Sirius.ToolboxUI/Program.cs`
- Delete: `src/Sirius.ToolboxUI/MasterDataSyncForm.cs`
- Delete/modify: `src/Sirius.Toolbox/Master/Sync/**` and official-only API/auth/download files discovered by reference search
- Modify: `src/Sirius.ToolboxUI/Sirius.ToolboxUI.csproj`, `src/Sirius.Toolbox/Sirius.Toolbox.csproj`
- Modify/delete: official-sync documentation under `docs/master`, `docs/toolboxui.md`, `docs/asset/**`, `README.md`, `PROJECT_CONTENTS.md`
- Test: `tests/Sirius.Toolbox.Tests/OfficialSyncRemovalTests.cs`

**Interfaces:**
- Consumes: existing `R2SyncForm`, `R2AssetSyncService`, and local MasterMemory editor.
- Produces: a build with no official sync types or UI references; homepage still exposes R2 sync and Master editor.

- [ ] **Step 1: Write the failing reference test** asserting homepage labels contain R2 sync and do not contain official sync, and that the ToolboxUI assembly has no `MasterDataSyncForm` type.
- [ ] **Step 2: Run `dotnet test --no-restore --filter OfficialSyncRemovalTests` and verify it fails against current references.**
- [ ] **Step 3: Remove the homepage button/window registration and delete official-only implementation files.** Use `rg` to trace every remaining reference before deleting a service or project reference; preserve any type used by R2/local editing.
- [ ] **Step 4: Update docs and startup self-checks to describe local editing and R2 publication only.**
- [ ] **Step 5: Run the filtered test and `dotnet build SiriusTools.sln --no-restore`; expected result is PASS, 0 warnings, 0 errors.**
- [ ] **Step 6: Commit with `refactor: remove official masterdata sync`.**

### Task 2: Add creation batch domain and service

**Files:**
- Create: `src/Sirius.Toolbox/Master/Creation/MasterCreationModels.cs`
- Create: `src/Sirius.Toolbox/Master/Creation/MasterCreationDefinitions.cs`
- Create: `src/Sirius.Toolbox/Master/Creation/MasterCreationService.cs`
- Test: `tests/Sirius.Toolbox.Tests/MasterCreationServiceTests.cs`

**Interfaces:**
- `MasterCreationDraft(string Kind, IReadOnlyList<MasterCreationRecord> Records, bool SkipResourceChecks)`.
- `MasterCreationRecord(string TableName, string PrimaryKey, IReadOnlyDictionary<string,string?> Fields)`.
- `MasterCreationPreview(IReadOnlyList<MasterCreationRecord> Records, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)`.
- `MasterCreationService.CreatePreview(string databasePath, MasterCreationDraft draft): MasterCreationPreview`.
- `MasterCreationService.Apply(string databasePath, MasterCreationDraft draft, string outputPath): MasterDatabaseVerification`.
- `MasterCreationService.AllocateNextId(string databasePath, string tableName, long minimum): long`.

- [ ] **Step 1: Write failing tests** for automatic ID allocation, manual duplicate detection, JSON scalar conversion, foreign-key validation, resource-path validation, and atomic rollback.
- [ ] **Step 2: Run the tests and verify they fail because the creation types do not exist.**
- [ ] **Step 3: Implement schema-driven field conversion and ID allocation.** Read `GetSchema`/`GetRecord`, construct a JSON object for each record, and reject read-only or unknown fields.
- [ ] **Step 4: Implement `CreatePreview` validation.** Validate table existence, primary keys, required values, date ordering, foreign keys including intra-batch references, and resource paths unless explicitly skipped.
- [ ] **Step 5: Implement `Apply` using a temporary copy.** Add records in dependency order through `MasterMemoryDatabaseService.AddRecord`, verify the resulting database, atomically write `outputPath`, and delete the temp directory on success or failure.
- [ ] **Step 6: Run service tests; expected result is PASS.**
- [ ] **Step 7: Commit with `feat: add atomic master creation service`.**

### Task 3: Add card, event, and music creation definitions

**Files:**
- Modify: `src/Sirius.Toolbox/Master/Creation/MasterCreationDefinitions.cs`
- Test: `tests/Sirius.Toolbox.Tests/MasterCreationDefinitionsTests.cs`

**Interfaces:**
- `MasterCreationDefinitions.Card`, `.Event`, `.Music` each expose a factory that returns a `MasterCreationDraft` from typed input maps.
- Definitions declare table order, primary-key fields, foreign-key bindings, required fields, and resource-path fields.

- [ ] **Step 1: Inspect generated model properties for `CharacterMaster`, `CharacterBaseMaster`, `MusicMaster`, `EventMaster`, and each selected event subtype; record exact writable names and key types in tests.**
- [ ] **Step 2: Write failing tests** asserting card/event/music drafts contain the expected root tables, generated IDs, and foreign-key values.
- [ ] **Step 3: Implement the three definitions without inventing fields absent from generated models.** Event subtype selection must be explicit; music and card definitions must reject incomplete required associations.
- [ ] **Step 4: Add tests for missing required fields, invalid event subtype, and missing resource paths.**
- [ ] **Step 5: Run the definition tests and commit with `feat: define custom master creation workflows`.**

### Task 4: Build the self-contained creation UI

**Files:**
- Create: `src/Sirius.ToolboxUI/MasterCreationPage.cs`
- Modify: `src/Sirius.ToolboxUI/MasterOperationsForm.cs`
- Modify: `src/Sirius.ToolboxUI/TimedMasterOperationsPage.cs` only if shared controls are needed
- Test: `tests/Sirius.Toolbox.Tests/MasterCreationPageSmokeTests.cs`

**Interfaces:**
- `MasterCreationPage(Func<string?> getWorkingPath, Func<MasterCreationDraft,Task> applyDraft)`.
- The page exposes three buttons (卡面/活动/音乐), schema-driven field editor, preview panel, error list, and submit callback.

- [ ] **Step 1: Write the failing smoke test** that constructs `MasterOperationsForm`, finds the 自制内容 tab and three creation buttons, and confirms no database is required at construction time.
- [ ] **Step 2: Implement the page with dynamic fields and JSON fallback.** Keep UI updates on the UI thread and disable submit while preview/apply is running.
- [ ] **Step 3: Add preview/confirm flow.** Display all tables, primary keys, changed fields, warnings, and errors before invoking the form callback.
- [ ] **Step 4: Wire `MasterOperationsForm` to apply a draft through `MasterCreationService` against a temp output, replace `_workingPath` only after verification, refresh all pages, and mark `_dirty`.**
- [ ] **Step 5: Run the smoke test and full build; commit with `feat: add custom master creation ui`.**

### Task 5: Documentation, integration verification, and final cleanup

**Files:**
- Modify: `README.md`, `PROJECT_CONTENTS.md`, `docs/toolboxui.md`, `docs/master/README.md`, `docs/master/architecture.md`
- Modify: `VALIDATION.md`
- Test: existing full test suite plus manual temp-database verification

- [ ] **Step 1: Document the custom creation workflow, supported resource-path behavior, and local R2 publication flow.** Remove remaining claims that official download is required.
- [ ] **Step 2: Run `rg` for official sync names and verify only historical references explicitly marked as removed remain.**
- [ ] **Step 3: Run `dotnet test --no-restore` and `dotnet build SiriusTools.sln --no-restore`; expected result is PASS, 0 warnings, 0 errors.**
- [ ] **Step 4: Use a temporary verified database to create one music, event, and card draft, reopen the output, and assert all created rows exist.**
- [ ] **Step 5: Commit with `docs: document custom master workflows`.**
