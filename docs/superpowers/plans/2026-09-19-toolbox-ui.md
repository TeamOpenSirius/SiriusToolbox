# ToolboxUI 统一图形界面实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将现有主数据编辑器、Chart 工具和 Episode 工具整合为一个带开始中心和三个独立工具窗口的 `Sirius.ToolboxUI` 应用。

**Architecture:** 保留现有 CLI 项目作为命令行入口，把 Chart/Episode 的文件处理逻辑提取为 `Sirius.AssetTool` 中的公共服务，CLI 与 WinForms 共用服务。将现有 MasterUI 项目迁移为 ToolboxUI，并新增启动中心、Chart 窗口和 Episode 窗口；每个窗口只负责参数收集、异步执行和中文展示。

**Tech Stack:** .NET 10, C#, WinForms, `Sirius.Tooling.Core`, `Sirius.MasterData`, existing `Sirius.AssetTool` chart and episode codecs.

**Spec:** `docs/superpowers/specs/2026-09-19-toolbox-ui-design.md`

## Global Constraints

- GUI project targets `net10.0-windows` and uses WinForms.
- GUI-visible text is Chinese; file formats, protocol field names, command-line switches, and internal identifiers remain unchanged.
- CLI projects remain runnable and keep their existing command syntax.
- GUI calls public in-process services; it does not start `Sirius.AssetTool.exe` or `Sirius.MasterTool.exe` as child processes.
- Existing dirty-worktree changes are preserved; do not use reset/checkout or overwrite unrelated files.
- Existing file overwrite and temporary working-copy behavior remains in force.

## Review Focus

- Repeated clicks on a launcher button must activate one existing child window rather than create unbounded duplicate windows; test by opening each child twice through the launcher.
- Chart and Episode file paths with spaces and non-ASCII characters must pass through the service unchanged; test with temporary paths containing Chinese characters.
- Existing output files must be rejected unless the GUI's overwrite option is enabled; test both branches for Chart and Episode operations.
- Chart keys must not appear in the result log or exception display; test the encode failure/success log projection.
- Closing the launcher must close child forms and release the MasterData temporary working copy; test form ownership/close behavior and run the application startup smoke test.

---

### Task 1: Rename the GUI project and preserve the current MasterData editor

**Files:**
- Move: `src/Sirius.MasterUI/` → `src/Sirius.ToolboxUI/`
- Modify: `src/Sirius.ToolboxUI/Sirius.MasterUI.csproj` → `src/Sirius.ToolboxUI/Sirius.ToolboxUI.csproj`
- Modify: `src/Sirius.ToolboxUI/Program.cs`
- Rename/modify: `src/Sirius.ToolboxUI/MainForm.cs` → `src/Sirius.ToolboxUI/MasterToolForm.cs`
- Modify: `SiriusTools.sln`

**Interfaces:**
- Produces `Sirius.ToolboxUI` assembly and namespace for later windows.
- `MasterToolForm(string? startupPath)` keeps the existing MasterData editor constructor contract.

- [x] **Step 1: Record the current baseline**

Run:

```powershell
dotnet build
```

Expected: the current workspace builds successfully, except for any file-lock failure caused by an already-running UI process; terminate only that process before continuing.

- [x] **Step 2: Move the GUI project directory and verify the target**

Move the exact directory `src/Sirius.MasterUI` to `src/Sirius.ToolboxUI`, verify that `src/Sirius.ToolboxUI/Sirius.MasterUI.csproj` exists, and verify that the destination did not previously exist.

- [x] **Step 3: Rename project metadata and the editor form**

Change the project file to `Sirius.ToolboxUI.csproj`, set `AssemblyName` and `RootNamespace` to `Sirius.ToolboxUI`, rename `MainForm` to `MasterToolForm`, and update the existing `Program` entry point to instantiate `MasterToolForm`.

- [x] **Step 4: Update the solution project path and display name**

Replace the old project path/name in `SiriusTools.sln` with `src\Sirius.ToolboxUI\Sirius.ToolboxUI.csproj` and `Sirius.ToolboxUI`.

- [x] **Step 5: Build the renamed project**

Run:

```powershell
dotnet build .\src\Sirius.ToolboxUI\Sirius.ToolboxUI.csproj
```

Expected: 0 warnings and 0 errors, with the existing MasterData editor still starting when run directly.

### Task 2: Extract reusable Chart operations

**Files:**
- Create: `src/Sirius.AssetTool/Charts/ChartToolService.cs`
- Modify: `src/Sirius.AssetTool/Charts/ChartCommands.cs`
- Modify: `src/Sirius.AssetTool/Sirius.AssetTool.csproj` only if public API metadata requires it

**Interfaces:**

Create these public types in `Sirius.AssetTool.Charts`:

```csharp
public sealed record ChartConvertOptions(bool IgnoreWaveOffset, bool Strict);
public sealed record ChartTextResult(string OutputPath, int NoteCount, IReadOnlyList<string> Warnings);
public sealed record ChartEncodeResult(string OutputPath, string? TextOutputPath, int NoteCount, IReadOnlyList<string> Warnings);
public sealed record ChartDecodeResult(string OutputPath);
public sealed record ChartSelfTestResult(int EncodedLength);
public sealed class ChartToolService
{
    public ChartTextResult ConvertToText(string inputPath, string outputPath, ChartConvertOptions options);
    public ChartEncodeResult Encode(string inputPath, string outputPath, string key, string? textOutputPath, ChartConvertOptions options);
    public ChartDecodeResult Decode(string inputPath, string outputPath, string key);
    public ChartSelfTestResult SelfTest(string key = "0123456789abcdef0123456789abcdef");
}
```

- [x] **Step 1: Add a service-level red test harness**

Add a small non-network executable test harness under `tests/Sirius.AssetTool.Tests` that creates a temporary SUS file, calls `ChartToolService.ConvertToText`, asserts the output exists and contains the converted chart line, and exits nonzero on failure. Reference `Sirius.AssetTool`; do not add a UI dependency.

- [x] **Step 2: Run the harness before implementation**

Run the harness and confirm it fails because `ChartToolService` and its result types do not yet exist.

- [x] **Step 3: Implement `ChartToolService` by moving the existing command logic**

Reuse `SusParser`, `SusToSiriusConverter`, `SiriusChartCrypto`, and the existing chart text formatting. Preserve the default key behavior only in CLI parsing; the service requires an explicit key for encode/decode and never logs it.

- [x] **Step 4: Refactor `ChartCommands` to delegate to the service**

Keep CLI option parsing and console output in `ChartCommands`; replace duplicated conversion/encode/decode/self-test work with service calls and preserve existing output wording and exit behavior.

- [x] **Step 5: Run the harness and existing CLI self-test**

Run:

```powershell
dotnet run --project .\tests\Sirius.AssetTool.Tests\Sirius.AssetTool.Tests.csproj
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- chart selftest
```

Expected: both exit 0.

### Task 3: Extract reusable Episode operations

**Files:**
- Create: `src/Sirius.AssetTool/Episodes/EpisodeToolService.cs`
- Modify: `src/Sirius.AssetTool/Episodes/EpisodeCommands.cs`
- Modify: `tests/Sirius.AssetTool.Tests/Program.cs`

**Interfaces:**

Create public records and service methods matching the existing operations:

```csharp
public sealed record EpisodePackResult(string OutputPath, string EpisodeId, int DetailCount, long ByteCount);
public sealed record EpisodeBatchItemResult(string InputPath, string? OutputPath, bool Succeeded, long ByteCount, string? Error);
public sealed record EpisodeBatchResult(IReadOnlyList<EpisodeBatchItemResult> Items, string OutputDirectory);
public sealed record EpisodeUnpackResult(string OutputPath, int DetailCount);
public sealed record EpisodeInspectResult(string InputPath, long ByteCount, int ReplacementCount, bool IsProbablyCorrupt, bool Decoded, int DetailCount, string? Error);
public sealed class EpisodeToolService
{
    public EpisodePackResult Pack(string inputPath, string? outputPath, bool overwrite);
    public EpisodeBatchResult PackDirectory(string inputDirectory, string? outputDirectory, bool overwrite);
    public EpisodeUnpackResult Unpack(string inputPath, string? outputPath, bool overwrite);
    public EpisodeBatchResult UnpackDirectory(string inputDirectory, string? outputDirectory, bool overwrite);
    public EpisodeInspectResult Inspect(string inputPath);
}
```

- [x] **Step 1: Add failing service harness cases**

Extend the test harness with a valid temporary Episode JSON fixture for `Pack`, a round-trip `Unpack` assertion, and an existing-output rejection assertion.

- [x] **Step 2: Run the harness and confirm the new cases fail**

The expected failure is the missing `EpisodeToolService` API, not a fixture or path error.

- [x] **Step 3: Implement `EpisodeToolService` using existing codecs**

Move the file validation, overwrite checks, corruption detection, packing, unpacking, and inspection logic from `EpisodeCommands` into the service. Return structured results instead of writing to console; preserve all existing checks.

- [x] **Step 4: Refactor `EpisodeCommands` to project service results to console**

Preserve existing command names, options, Chinese CLI messages, exit codes, and per-file batch reporting.

- [x] **Step 5: Run the service harness and Episode CLI help commands**

Run:

```powershell
dotnet run --project .\tests\Sirius.AssetTool.Tests\Sirius.AssetTool.Tests.csproj
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- episode --help
```

Expected: exit 0 for both commands.

### Task 4: Add the ToolboxUI start center and child-window lifecycle

**Files:**
- Create: `src/Sirius.ToolboxUI/ToolboxHomeForm.cs`
- Modify: `src/Sirius.ToolboxUI/Program.cs`
- Modify: `src/Sirius.ToolboxUI/Sirius.ToolboxUI.csproj`
- Modify: `SiriusTools.sln`

**Interfaces:**

`ToolboxHomeForm` owns nullable references to `MasterToolForm`, `ChartToolForm`, and `EpisodeToolForm`, with one `OpenOrActivate<T>` helper that reuses a non-disposed child form.

- [x] **Step 1: Add a launcher smoke assertion**

Add a testable helper or internal lifecycle method whose behavior is: first activation creates one form, second activation returns the same instance, and closing the child clears the stored reference. The test harness can exercise this helper without creating native controls if it is kept as a small `ChildWindowRegistry` class.

- [x] **Step 2: Verify the lifecycle test fails before implementation**

Run the harness and confirm the missing registry/helper failure.

- [x] **Step 3: Implement `ChildWindowRegistry` and `ToolboxHomeForm`**

Build a Chinese start window with three buttons, use `Show` plus `Activate` for child forms, set `Owner` to the home form, and close all registered children from `OnFormClosing`.

- [x] **Step 4: Update `Program` to launch `ToolboxHomeForm`**

Pass through an optional startup database path by opening the MasterTool child after the home form is shown, preserving the existing command-line convenience.

- [x] **Step 5: Build and launch the start center**

Run:

```powershell
dotnet build .\src\Sirius.ToolboxUI\Sirius.ToolboxUI.csproj
dotnet .\src\Sirius.ToolboxUI\bin\Debug\net10.0-windows\Sirius.ToolboxUI.dll
```

Expected: the process stays in the WinForms message loop without a constructor exception.

### Task 5: Adapt the MasterData editor into `MasterToolForm`

**Files:**
- Modify: `src/Sirius.ToolboxUI/MasterToolForm.cs`

- [x] **Step 1: Preserve the existing MasterData startup path**

Keep `MasterToolForm(string? startupPath)` and the current temporary working-copy behavior; move any startup-path opening from `Program` into the child-window activation path.

- [x] **Step 2: Update title, class references, and launcher-visible Chinese labels**

Retain the previously applied Chinese UI, JSON Unicode encoder, Dock ordering fix, and SplitContainer layout guard.

- [x] **Step 3: Build and run with a database argument**

Run the ToolboxUI executable with a valid database path and verify the MasterData child opens without the prior splitter or toolbar-overlap exceptions.

### Task 6: Build the Chart tool window

**Files:**
- Create: `src/Sirius.ToolboxUI/ChartToolForm.cs`
- Modify: `src/Sirius.ToolboxUI/ToolboxHomeForm.cs`
- Modify: `src/Sirius.ToolboxUI/Sirius.ToolboxUI.csproj`

- [x] **Step 1: Add controls and validation**

Create Chinese controls for operation selection, input/output file pickers, key input, strict and wave-offset checkboxes, intermediate-text output, overwrite confirmation, execute button, and multiline log output.

- [x] **Step 2: Wire file dialogs and service calls**

Call `ChartToolService` asynchronously via `Task.Run`, disable controls during execution, append note counts/warnings/output paths to the log, and never append the key.

- [x] **Step 3: Add self-test and error projection**

Run the service self-test from the form and render exceptions in Chinese. Validate required paths and 32-byte key length before starting work.

- [x] **Step 4: Build the UI project**

Run `dotnet build .\src\Sirius.ToolboxUI\Sirius.ToolboxUI.csproj`; expected 0 warnings and 0 errors.

### Task 7: Build the Episode tool window

**Files:**
- Create: `src/Sirius.ToolboxUI/EpisodeToolForm.cs`
- Modify: `src/Sirius.ToolboxUI/ToolboxHomeForm.cs`
- Modify: `src/Sirius.ToolboxUI/Sirius.ToolboxUI.csproj`

- [x] **Step 1: Add operation controls**

Create Chinese controls for pack, batch pack, batch unpack, unpack, inspect, file/directory path pickers, overwrite checkbox, execute button, and multiline log output.

- [x] **Step 2: Wire service calls and result rendering**

Use `Task.Run`, disable execution controls while running, render structured results including batch successes/failures and inspect diagnostics, and show Chinese errors without terminating the process.

- [x] **Step 3: Verify all operations through the GUI service boundary**

Use the service harness fixtures for pack, pack-dir, unpack-dir, unpack, and inspect; then build the UI project to catch event-handler and nullable errors.

### Task 8: Final solution integration, documentation, and verification

**Files:**
- Modify: `SiriusTools.sln`
- Modify: `README.md`
- Modify: `docs/masterui.md` → rename/update as `docs/toolboxui.md`
- Modify: `scripts/smoke-test.ps1`
- Modify: `PROJECT_CONTENTS.md` and `VALIDATION.md` if their generated/project lists mention the old UI name

- [x] **Step 1: Update user-facing documentation**

Document the ToolboxUI launch command, the three independent windows, and the retained CLI commands. Remove stale instructions that launch `Sirius.MasterUI` as the unified GUI.

- [x] **Step 2: Update the smoke test**

Run the CLI help/self-test paths without launching a GUI; add a build assertion for `Sirius.ToolboxUI` and keep the test safe on headless environments.

- [x] **Step 3: Run the complete verification set**

Run:

```powershell
dotnet build .\SiriusTools.sln
dotnet run --project .\tests\Sirius.AssetTool.Tests\Sirius.AssetTool.Tests.csproj
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- chart selftest
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- episode --help
.\scripts\smoke-test.ps1
```

Expected: every command exits 0, the solution has 0 warnings and 0 errors, and no UI process remains running after manual startup verification.

- [x] **Step 4: Inspect the final status and diff**

Run `git status --short`, `git diff --check`, and inspect all changed source/project/documentation files. Report any pre-existing unrelated modifications separately.
