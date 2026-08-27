# Sirius.MasterTool

`Sirius.MasterTool` synchronizes World Dai Star MasterData and optionally exports the downloaded MasterMemory database into readable per-table JSON files.

## Capabilities

- fetch the current Environment configuration;
- register an account when no reusable LoginToken is available;
- authenticate and cache access credentials;
- retry authentication once when login returns HTTP 440;
- fetch `/api/data/master`;
- skip an unchanged MasterData version unless forced;
- resume interrupted database downloads with HTTP Range requests;
- keep a backup before replacing an existing `mastermemory.db`;
- calculate and store SHA-256 metadata;
- export MasterMemory tables through `table.json`;
- persist local synchronization state and publication metadata.

## Quick start

```powershell
dotnet run --project .\src\Sirius.MasterTool\Sirius.MasterTool.csproj -- --sync --dir output
```

Detailed documentation:

- [Usage](usage.md)
- [Authentication and State](authentication.md)
- [MasterMemory JSON Export](masterdata-json.md)
- [Output Layout](output-layout.md)
- [Architecture](architecture.md)
- [Troubleshooting](troubleshooting.md)
