# Master Operations Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a WinForms operations-oriented MasterMemory editor for events, exchange shops, gachas, and music while preserving the existing raw-table editor.

**Architecture:** Business operations live in `Sirius.Toolbox.Master.Operations` and use only `Sirius.MasterData.MasterMemoryDatabaseService` for reads/writes. A new `MasterOperationsForm` owns a working-copy lifecycle and delegates UI pages to focused controls. Multi-record edits are applied to a temporary DB and copied back only after verification succeeds.

**Tech Stack:** C# / .NET 10 / WinForms / Sirius.MasterData

**Spec:** `docs/superpowers/specs/2026-10-02-master-ops-editor-design.md`

## Global Constraints

- Keep the existing raw MasterData editor intact.
- Do not reimplement MasterMemory binary serialization.
- All writes go through `MasterMemoryDatabaseService.UpdateRecord`.
- Multi-row edits are atomic from the editor's point of view.
- Relation propagation is explicit by matching reference fields, then shown in preview before apply.

## Tasks

1. Add operation-plan models, dynamic MasterData record helpers, and atomic plan applier.
2. Add event/gacha timed-entity relation scanning and propagation.
3. Add exchange-shop nested item discovery and recursive expiry extension.
4. Add music unlock conversion helpers including `Buy(10)` / `ReadEpisodeAndBuy(11)` -> `Default(1)`.
5. Add WinForms operations editor pages and working-copy/save lifecycle.
6. Add Toolbox home entry and focused test helpers/documentation.
