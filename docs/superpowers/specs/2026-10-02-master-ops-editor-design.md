# SiriusToolbox Master Operations Editor Design

Date: 2026-10-02
Target: TeamOpenSirius/SiriusToolbox
Related models: TeamOpenSirius/SiriusData (`Sirius.MasterData` / `Sirius.Protocol`)

## Goal

Upgrade the existing MasterMemory editor into an operations-oriented GUI for routine WDS server maintenance. The primary workflows are Event, Exchange Shop, Gacha, and Music editing. Operators should edit human-readable business entities rather than raw MasterMemory tables while retaining the current generic table/JSON editor as a fallback.

Success criteria:

- Open one MasterMemory DB and use the existing working-copy/save flow.
- Browse/search Events, Exchange Shops, Gachas, and Music from dedicated views.
- Edit common fields with WinForms controls instead of raw JSON.
- Display linked MasterData rows that are affected by the selected business entity.
- Time-range changes can propagate to selected linked rows in one operation.
- Exchange-shop extension updates both the shop and all selected nested/related exchange things.
- Music unlock mode can be edited individually or in bulk, including a one-click `Buy/ReadEpisodeAndBuy -> Default` operation.
- All edits still go through `Sirius.MasterData.MasterMemoryDatabaseService` and generated MasterMemory models; no duplicate binary parser is introduced in Toolbox.
- Existing generic editor remains available for every table.

## Existing architecture to preserve

`MasterToolForm` already owns the file lifecycle: open DB -> create working copy -> verify -> edit -> save/save-as. It uses `MasterMemoryDatabaseService` for table discovery, schema, record listing, record lookup, writes, repacking and verification.

The UI project already references the sibling `SiriusData/src/Sirius.MasterData` project. This remains the single MasterMemory access layer.

## Architecture

### 1. UI shell

Keep `MasterToolForm` as the database/document shell. Add a top-level editor mode with:

- Operations
- Raw Tables

The existing table list, record grid, schema view and JSON editor move under `Raw Tables` without behavioral changes.

`Operations` hosts a new `MasterOperationsControl` rather than adding more business logic directly to `MasterToolForm`.

### 2. Operations domain layer

Add `Sirius.Toolbox/Master/Operations` with small services:

- `MasterOperationsService`
  - builds operation-facing projections from MasterMemory tables
  - resolves related rows
  - creates and applies change plans
- `MasterOperationPlan`
  - collection of table/key JSON patches
  - human-readable change summary
- `MasterOperationApplier`
  - applies a complete plan to a temporary DB chain
  - verifies the resulting DB
  - replaces the current working copy only after all patches succeed
- entity-specific adapters
  - `EventOperationsAdapter`
  - `ExchangeShopOperationsAdapter`
  - `GachaOperationsAdapter`
  - `MusicOperationsAdapter`

Adapters do not parse the MasterMemory binary themselves. They use `MasterMemoryDatabaseService.GetTables`, `GetSchema`, `ListRecords`, `GetRecord`, `UpdateRecord`, and related SiriusData APIs.

### 3. Relationship handling

Relationships are explicit, not guessed globally by reflection. Each adapter defines the known relationship paths relevant to its business entity, based on SiriusData field names and IDs.

The UI shows each related row with:

- table
- key
- display label
- current start/end time where present
- proposed value
- checkbox controlling whether the row participates in propagation

This avoids silently changing unrelated rows while still making routine operational edits one action.

### 4. Event editor

Event list columns:

- ID
- name
- type
- start
- end
- force end
- status (future/active/ended)

Detail panel:

- editable name and primary timing fields
- linked MasterData section
- `Set end time for linked rows` action
- common presets: +7 days, +30 days, custom date, far-future

The adapter resolves known event-linked rows and exposes them as selectable changes. The primary Event row is always included when its own field is edited.

### 5. Exchange Shop editor

Two-level master/detail view:

- shop list
- items for selected shop

Editable shop fields include name/display text and start/end timing where available.

Bulk actions:

- extend shop to selected date
- extend shop and all items
- extend only checked items

The change preview must show both the shop row and every item row being changed. This directly covers the current operational case where a shop remains visible but redemption is rejected because an ExchangeShopThing end time still expired.

### 6. Gacha editor

Gacha list with:

- ID
- display name
- type/category
- start/end
- status

Detail view exposes the core Gacha row and known linked presentation/banner/text rows. Timing propagation is selectable just like Events. Descriptive fields remain editable on their owning row rather than being duplicated into unrelated linked rows.

### 7. Music editor

Music list with:

- ID
- title
- cover/original category
- long-version flag
- unlock type
- unlock text

Detail editor uses enum-aware ComboBoxes for unlock condition rather than numeric entry.

Bulk actions:

- checked songs -> Default
- all Buy -> Default
- all Buy + ReadEpisodeAndBuy -> Default

When converting to Default, the operation clears obsolete unlock-condition value/text fields that are only meaningful for shop/episode unlocking.

### 8. Change preview and safety

Every multi-table action produces a preview before applying:

- table/key
- field
- old value
- new value

Applying a plan:

1. clone current working DB to an operation temp path
2. apply patches sequentially through SiriusData APIs
3. run `MasterMemoryDatabaseService.Verify`
4. if all succeed, replace the working DB
5. mark the document dirty and refresh affected views
6. on failure, delete the temp output and keep the original working DB untouched

Single-record edits may use the same plan path for consistent behavior.

### 9. UI layout

`MasterOperationsControl` uses a left navigation list and right content area:

- Overview
- Events
- Exchange Shops
- Gachas
- Music

Each page follows the same structure:

- top search/filter bar
- upper grid/list
- lower detail editor
- linked-data grid or item grid
- change preview/apply buttons

Use existing WinForms styling and controls; do not introduce a second UI framework.

### 10. Testing

Add tests in `tests/Sirius.Toolbox.Tests` for:

- relationship mapping from fixture/sample MasterMemory
- operation plan generation
- exchange shop + thing time propagation
- event linked-time propagation
- music Buy/ReadEpisodeAndBuy -> Default bulk conversion
- atomic behavior: failed patch leaves original working DB unchanged
- verification of the final generated DB

UI event wiring itself is kept thin; business behavior is tested in `Sirius.Toolbox` services.

## Files expected to change

Existing:

- `src/Sirius.ToolboxUI/MasterToolForm.cs`
- `src/Sirius.ToolboxUI/Sirius.ToolboxUI.csproj` only if needed
- `tests/Sirius.Toolbox.Tests/Program.cs`
- documentation/README entries as appropriate

New:

- `src/Sirius.Toolbox/Master/Operations/MasterOperationsService.cs`
- `src/Sirius.Toolbox/Master/Operations/MasterOperationPlan.cs`
- `src/Sirius.Toolbox/Master/Operations/MasterOperationApplier.cs`
- `src/Sirius.Toolbox/Master/Operations/EventOperationsAdapter.cs`
- `src/Sirius.Toolbox/Master/Operations/ExchangeShopOperationsAdapter.cs`
- `src/Sirius.Toolbox/Master/Operations/GachaOperationsAdapter.cs`
- `src/Sirius.Toolbox/Master/Operations/MusicOperationsAdapter.cs`
- `src/Sirius.ToolboxUI/MasterOperationsControl.cs`
- focused UI page/control files if the main control becomes too large

## Non-goals for this iteration

- Replacing the generic raw-table editor.
- Building a generic graph editor for every MasterMemory relationship.
- Editing binary assets referenced by MasterData.
- Publishing/restarting the production server from the editor.
- Reimplementing MasterMemory serialization outside SiriusData.

