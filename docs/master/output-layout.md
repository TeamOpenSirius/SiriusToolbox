# Output Layout

With the default `--dir output`, `Sirius.MasterTool` writes:

```text
output/
├─ state.json
├─ publication.json
└─ master/
   ├─ mastermemory.db
   ├─ mastermemory.db.bck       optional
   ├─ mastermemory.db.part      present only during/incomplete download
   ├─ manifest.json
   └─ json/
      ├─ .complete
      ├─ <TableName>.json
      └─ ...
```

## `state.json`

Private runtime state used to resume synchronization and reuse credentials. It includes tokens, endpoints, client/version parameters, current MasterData state, and update time.

See [Authentication and State](authentication.md).

## `master/mastermemory.db`

The downloaded MasterMemory database from the current MasterData manifest.

The final download URL is built from:

- `EnvironmentResult.MasterDataUrl`;
- the manifest `Uri`;
- the manifest SAS token, when present.

## `master/mastermemory.db.part`

Temporary/resumable download file. Existing partial data is used as the starting offset for an HTTP Range request.

The file is moved to `mastermemory.db` after a successful download.

## `master/mastermemory.db.bck`

When a refresh is required and `mastermemory.db` already exists, the current database is copied to this path before downloading the replacement.

Only one backup path is maintained; a later refresh replaces it.

## `master/manifest.json`

Local metadata generated after the database has been downloaded or confirmed current.

It contains:

- `Version`;
- `Uri`;
- `PublishTimestamp`;
- `AssetVersion`;
- `AssetUrl`;
- `StaticContentUrl`;
- `PhotoContentUrl`;
- local `PublishedAt` time;
- database `Size`;
- lowercase SHA-256 in `Sha256`.

## `master/json/`

When JSON export is enabled, every MasterMemory table is written to:

```text
master/json/<TableName>.json
```

Table names come from the MasterMemory header.

## `master/json/.complete`

Export completion/version marker:

```text
object-schema-v1:<MasterDataVersion>
```

It allows an unchanged JSON export to be skipped independently from the database download decision.

## `publication.json`

Machine-readable synchronization publication metadata. The current model uses `SchemaVersion = 1` and includes:

| Field | Meaning |
| --- | --- |
| `MasterDataVersion` | Current MasterData version. |
| `SourceMasterDataVersion` | Source version recorded for the current database. |
| `MasterDataPublishTimestamp` | Manifest publish timestamp. |
| `MasterDataUri` | Manifest content URI. |
| `MasterDataFile` | Local database path. |
| `MasterDataSha256` | SHA-256 of the local database. |
| `MasterJsonDirectory` | Per-table JSON directory. |
| `AssetVersion` | Environment asset version. |
| `AssetSourceUrl` | Environment asset URL. |
| `StaticContentSourceUrl` | Environment static-content URL. |
| `CdnManifest` | Newest `assets/manifests/cdn_*.json` under the output root, when present. |
| `PublishedAt` | Time the publication file was written. |

`MasterDataPolicy` and `MasterIndexDatabase` are currently emitted as empty strings.
