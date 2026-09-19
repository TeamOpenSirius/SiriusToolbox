# Sirius.MasterTool Usage

## Requirements

- .NET 10 SDK
- `lib/Sirius.Protocol.dll` supplied with this repository

Build:

```powershell
dotnet restore .\SiriusTools.sln
dotnet build .\SiriusTools.sln -c Release
```

## Synchronize MasterData

```powershell
Sirius.MasterTool.exe sync --dir output
```

The legacy `Sirius.MasterTool.exe --sync ...` form is also accepted.

Common sync options:

| Option | Default | Description |
| --- | --- | --- |
| `--dir <path>` | `output` | Root output directory. |
| `--api <url>` | official API | Bootstrap API. |
| `--login-token <token>` | — | Existing LoginToken. |
| `--access-token <token>` | — | Existing Bearer token. |
| `--register-name <name>` | `ArchiveUser` | Registration name. |
| `--app-version <version>` | `2.30.1` | Public application version. |
| `--auth-version-suffix <text>` | `.486` | Authentication version suffix. |
| `--game-version <number>` | `2` | Game protocol version. |
| `--platform <name>` | `google-play` | `X-Platform`. |
| `--fm <value>` | `0` | `X-FM`. |
| `--force` | false | Redownload/re-export. |
| `--no-json` | false | Skip per-table JSON export. |
| `--insecure` | false | Disable TLS certificate validation for controlled debugging only. |

## Inspect MasterMemory

List tables:

```powershell
Sirius.MasterTool.exe db tables mastermemory.db
Sirius.MasterTool.exe db tables mastermemory.db --contains Music
```

Show the actual generated model fields and primary key:

```powershell
Sirius.MasterTool.exe db schema mastermemory.db MusicMaster
```

List records (JSON output, 50 rows by default):

```powershell
Sirius.MasterTool.exe db list mastermemory.db MusicMaster --offset 0 --limit 20
```

Read by primary key. Single-column keys accept the value directly:

```powershell
Sirius.MasterTool.exe db get mastermemory.db MusicMaster --key 1001
```

Composite keys use `Field=value;Field2=value`:

```powershell
Sirius.MasterTool.exe db get mastermemory.db SomeTable --key "Id=1001;Type=2"
```

## Add a record

From JSON file:

```powershell
Sirius.MasterTool.exe db add mastermemory.db MusicMaster `
  --json .\music-new.json `
  -o mastermemory.modified.db
```

Or construct/override top-level fields on the CLI:

```powershell
Sirius.MasterTool.exe db add mastermemory.db ExampleMaster `
  --set Id=900001 `
  --set Name="My Record" `
  -o mastermemory.modified.db
```

`--set` values are parsed as JSON when possible. Numbers, booleans, arrays, objects, and quoted strings can therefore preserve their intended types.

## Update a record

Patch fields from JSON:

```powershell
Sirius.MasterTool.exe db update mastermemory.db MusicMaster `
  --key 1001 `
  --data '{"Name":"Updated name"}' `
  -o mastermemory.modified.db
```

Or use repeated setters:

```powershell
Sirius.MasterTool.exe db update mastermemory.db MusicMaster `
  --key 1001 `
  --set Name="Updated name" `
  --set SortOrder=100 `
  -o mastermemory.modified.db
```

## Delete a record

```powershell
Sirius.MasterTool.exe db delete mastermemory.db MusicMaster --key 1001 -o mastermemory.modified.db
```

## In-place editing

For add/update/delete, replace `-o ...` with `--in-place`:

```powershell
Sirius.MasterTool.exe db update mastermemory.db MusicMaster --key 1001 --set Name="Updated" --in-place
```

The command creates `mastermemory.db.bak` before replacing the input. The modified database is fully reloaded through `Sirius.Protocol.Shared.MemoryDatabase` before it is accepted.

## Export JSON

```powershell
Sirius.MasterTool.exe db export-json mastermemory.db .\master-json
```

The exporter uses the generated `Sirius.Protocol` table models directly. No external table schema is required.

## Localization workflow

Export string cells:

```powershell
Sirius.MasterTool.exe db export-text mastermemory.db translation.csv --japanese-only
```

Write the `translation` column back:

```powershell
Sirius.MasterTool.exe db build-text mastermemory.db translation.csv mastermemory.zh.db
```

The localization writer validates primary keys and source strings against the input database so stale CSV files cannot silently patch the wrong record.

## Validation

```powershell
Sirius.MasterTool.exe db verify mastermemory.db
Sirius.MasterTool.exe db roundtrip mastermemory.db mastermemory.copy.db
```
