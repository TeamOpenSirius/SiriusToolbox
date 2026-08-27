# Sirius.MasterTool Architecture

`Sirius.MasterTool` separates command-line and network orchestration from shared protocol, export, and persistence code.

## Project responsibilities

### `Sirius.MasterTool`

- `Program.cs`: synchronization workflow and output coordination;
- `Cli/Cli.cs`: command-line parsing and help;
- `Configuration/DownloaderOptions.cs`: runtime option model and defaults;
- `Networking/SiriusApiClient.cs`: HTTP requests, headers, MessagePack transport, and resumable file download.

### `Sirius.Tooling.Core`

- `Master/Protocol/`: MessagePack request and response models;
- `Master/Serialization/`: MessagePack request encoding and response payload decoding;
- `Master/Export/`: MasterMemory parsing and schema-based JSON mapping;
- `Master/Persistence/`: `state.json` and `publication.json` models/stores;
- `Schema/table.json`: field and enum schema used during JSON export.

## Synchronization flow

### 1. Resolve paths and state

The selected output directory is converted to an absolute path. The tool creates `master/` and loads `state.json` when present.

### 2. Fetch Environment

`POST /api/Environment` is sent to the bootstrap API with `applicationVersion` and `gameVersion` query parameters.

Environment supplies the effective API endpoint and content/version values used by later requests.

### 3. Resolve credentials

The tool selects supplied or saved credentials. A saved access token is accepted only when its game version and authentication application version match the current options.

When no LoginToken exists, an account is registered. When no access token exists, Authenticate is called.

### 4. Login

The tool sends `/api/Login` with the Bearer token. HTTP 440 triggers one Authenticate-and-retry cycle when a LoginToken is available.

### 5. Fetch MasterData manifest

`GET /api/data/master` returns:

- relative content URI;
- SAS token;
- MasterData version;
- publish timestamp.

The manifest version is then also sent as `X-MasterData-Version` on subsequent API requests.

### 6. Decide whether to download

The local version comes from `state.json`, falling back to `master/manifest.json` when needed.

The download is skipped when the version matches, the database exists, and `--force` is not set.

Before replacing an existing database, it is copied to `mastermemory.db.bck`.

### 7. Download with resume support

Downloads are written to `mastermemory.db.part`.

If a partial file exists, the HTTP client requests the remaining range. If the server ignores Range and responds with `200 OK`, the partial file is discarded and the response is written from the beginning. A `416 Requested Range Not Satisfiable` response is checked against the remote length; an already complete partial file is promoted directly to the final destination.

### 8. Write local metadata

After the database is available, the tool computes SHA-256 and writes `master/manifest.json`. `state.json` is updated with the current MasterData version.

### 9. Export JSON

Unless `--no-json` is set, the tool checks `master/json/.complete`. When export is required, `MasterMemoryExporter` reads the database and writes one JSON file per table using `table.json` for field mapping.

### 10. Publish synchronization metadata

`publication.json` records the current MasterData file, version, hash, source metadata, JSON output path, Environment asset information, and the newest `assets/manifests/cdn_*.json` path if one exists below the output root.

## Error and cancellation behavior

`Ctrl+C` cancels the asynchronous workflow through a `CancellationTokenSource` and returns exit code `130`.

Other unhandled exceptions are printed with their stack trace and return exit code `1`.
