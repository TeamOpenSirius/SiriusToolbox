# MasterMemory JSON Export

MasterMemory JSON export is generated from `Sirius.Protocol.Shared.MemoryDatabase` and its generated table metadata.

There is no separate `table.json` schema in the Toolbox. Table names, row types, property names, MessagePack fields, and MasterMemory primary indexes come from the same `Sirius.Protocol.dll` used to load and rebuild the database.

During `sync`, JSON is written under `output/master/json/`. It can also be generated explicitly:

```powershell
Sirius.MasterTool.exe db export-json mastermemory.db master-json
```

Each MasterMemory table becomes one `<TableName>.json` file containing an array of typed records. Enum values are emitted by name; byte arrays are Base64 strings.
