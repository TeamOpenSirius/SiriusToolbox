# Sirius.MasterTool Troubleshooting

## `Sirius.Protocol` cannot be loaded

Ensure this file exists:

```text
lib/Sirius.Protocol.dll
```

The supplied assembly targets `net10.0`, so use a .NET 10 SDK/runtime.

## A table name is unknown

Use:

```powershell
Sirius.MasterTool.exe db tables mastermemory.db
```

Table names come directly from `MemoryDatabase.GetMetaDatabase()` and are case-sensitive when resolved by MasterMemory.

## A key cannot be parsed

Inspect the primary-key fields:

```powershell
Sirius.MasterTool.exe db schema mastermemory.db TableName
```

For a one-field key, `--key 123` is accepted. For composite keys use `--key "A=123;B=4"`.

## An update/add fails type conversion

Use `db schema` to inspect the generated property types. `--set` values are interpreted as JSON when possible, so strings containing JSON syntax should be quoted as JSON strings or supplied through `--json`/`--data`.

## The rewritten DB fails validation

The command will not accept a rewritten DB unless the full output loads through `Sirius.Protocol.Shared.MemoryDatabase`. For `--in-place`, the original `<input>.bak` remains available.
