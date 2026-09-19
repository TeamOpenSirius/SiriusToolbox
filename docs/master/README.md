# Sirius.MasterTool

`Sirius.MasterTool` synchronizes World Dai Star MasterData and provides typed CLI operations for `mastermemory.db`.

MasterMemory tables are loaded through the generated `Sirius.Protocol.Shared.MemoryDatabase` contained in `lib/Sirius.Protocol.dll`. The Toolbox no longer maintains a duplicate MasterMemory model or `table.json` schema.

## Capabilities

- synchronize the current official `mastermemory.db`;
- export every table to readable JSON using `Sirius.Protocol` property names and types;
- list tables, schemas, and records;
- read records by primary key;
- add, update, and delete records;
- rebuild only changed table blocks while preserving untouched table blocks;
- validate every rewritten database by loading it again through generated `MemoryDatabase`;
- export translatable strings to CSV and write translations back into MasterMemory.

## Quick start

```powershell
dotnet run --project .\src\Sirius.MasterTool\Sirius.MasterTool.csproj -- sync --dir output

dotnet run --project .\src\Sirius.MasterTool\Sirius.MasterTool.csproj -- db tables .\output\master\mastermemory.db
```

See [Usage](usage.md) for all CLI commands.
