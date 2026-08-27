# MasterMemory JSON Export

`Sirius.MasterTool` can export `mastermemory.db` into one formatted JSON file per MasterData table.

## MasterMemory layout

`MasterMemoryExporter` treats the database as a MessagePack stream.

1. The first MessagePack object is read as a header map.
2. Each header entry maps a table name to an offset/length pair.
3. The offset is relative to the byte position immediately after the header.
4. The table slice is decoded as MessagePack.
5. The decoded value is mapped through the table schema when a matching schema exists.
6. The mapped value is written to `<TableName>.json`.

Table payload decoding first tries standard MessagePack and then `Lz4BlockArray` MessagePack.

## Schema file

The canonical schema is:

```text
src/Sirius.Tooling.Core/Schema/table.json
```

`Sirius.MasterTool.csproj` links it into build output as:

```text
data/table.json
```

A different schema can be selected with:

```powershell
Sirius.MasterTool.exe --sync --table-schema D:\path\to\table.json
```

When JSON export is enabled and no schema can be resolved, synchronization fails with a schema-not-found error.

## Schema structure

Each top-level schema entry describes either a class or an enum.

A class definition contains fields with:

- positional MessagePack key;
- field name;
- declared type.

An enum definition maps numeric values to readable names.

This allows positional MessagePack rows such as:

```json
[1001, "Example", 3]
```

to be emitted as named JSON objects when a matching class schema exists.

## Supported mapping behavior

The mapper handles:

- classes;
- enums;
- nullable types;
- arrays;
- `byte[]` as Base64 strings;
- booleans;
- signed and unsigned integer types;
- floating-point values and decimals;
- strings;
- `DateTime`;
- `Guid`;
- generic nested arrays/maps when no specific schema mapping is available.

Unknown enum numeric values are preserved as their raw values during export.

## Output directory

JSON files are written under:

```text
<output>/master/json/
```

The exporter tracks every table written in the current run. Any top-level `*.json` file in that directory that does not correspond to a table in the current MasterMemory header is removed after a successful export.

## Completion marker

After a successful export, the tool writes:

```text
master/json/.complete
```

The current marker format is:

```text
object-schema-v1:<MasterDataVersion>
```

When that marker already matches the current MasterData version and `--force` is not set, JSON parsing is skipped.

## Disabling JSON export

Use:

```powershell
Sirius.MasterTool.exe --sync --no-json
```

to download and maintain `mastermemory.db` without reading the schema or producing per-table JSON.

## Schema compatibility

The schema must match the positional layout of the MasterData being exported. A table whose field layout no longer matches the schema may fail with:

```text
Failed to export master table '<TableName>'.
```

When failures are isolated to particular tables, check the corresponding `table.json` definitions and MessagePack key positions first.
