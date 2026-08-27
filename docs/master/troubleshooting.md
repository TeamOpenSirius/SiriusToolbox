# Sirius.MasterTool Troubleshooting

## The tool prints help and does not synchronize

Synchronization requires `--sync`.

```powershell
Sirius.MasterTool.exe --sync --dir output
```

Unknown options are rejected.

## Environment or Authenticate requests fail

Verify that the configured client parameters match the target service:

```text
--app-version
--auth-version-suffix
--game-version
--platform
--fm
```

Current defaults are:

```text
ApplicationVersion = 2.30.1
AuthenticationVersionSuffix = .486
GameVersion = 2
Platform = google-play
Fm = 0
```

The Authenticate payload also contains APK hash and application-signature values in `Networking/SiriusApiClient.cs`. If the service changes its accepted client parameters, those values may also require review.

## The tool says the cached access token uses different client parameters

The saved access token is only reusable when the saved `GameVersion` and `AuthenticationApplicationVersion` match the current options.

This message means the token was intentionally ignored. If a LoginToken is available, authentication continues with a new access token.

## Login fails with HTTP 440

The tool automatically authenticates again once and retries login when a LoginToken is available.

If the retry also fails:

1. confirm the LoginToken is valid;
2. verify the client version parameters;
3. inspect `state.json` for unexpected credential values;
4. if necessary, back up the state file and start with explicitly supplied credentials or a fresh registration.

## `table.json` cannot be found

Typical error:

```text
Master table schema was not found. Specify --table-schema or deploy data/table.json.
```

Check that the build output contains:

```text
data/table.json
```

or provide a schema explicitly:

```powershell
Sirius.MasterTool.exe --sync --table-schema D:\path\to\table.json
```

If JSON output is not needed, use `--no-json`.

## A MasterData table fails to export

Typical wrapper error:

```text
Failed to export master table '<TableName>'.
```

Check:

- database completeness;
- the table's offset and length in the MasterMemory header;
- the corresponding class/enum definition in `table.json`;
- positional MessagePack keys and nested type definitions.

A schema mismatch can affect a single table while the database itself remains readable.

## JSON is not regenerated

Inspect:

```text
master/json/.complete
```

If it equals:

```text
object-schema-v1:<current MasterDataVersion>
```

JSON export is considered current.

Use `--force` to rebuild it.

## `mastermemory.db` is not downloaded again

The download is skipped when the local version matches the remote manifest, the database exists, and `--force` is not set.

Use:

```powershell
Sirius.MasterTool.exe --sync --force
```

to request a refresh.

## `mastermemory.db.part` remains in the output directory

A `.part` file is an incomplete or not-yet-promoted download. The next synchronization attempt uses its size as an HTTP Range offset.

If the server does not honor Range, the tool automatically restarts the file from byte zero. Delete the partial file manually only when you have evidence that its contents are invalid.

## TLS certificate validation fails

Check system time, the certificate chain, proxies, and interception software first.

For controlled debugging only:

```powershell
Sirius.MasterTool.exe --sync --insecure
```

This disables certificate validation for the tool's HTTP client.

## `state.json` cannot be parsed

A malformed state file causes JSON deserialization to fail. Back up the file before replacing it because it can contain reusable credentials.

If the file cannot be recovered, move it out of the output directory and run with a supplied token or allow the tool to register an account.

## Verify database integrity

`master/manifest.json` records the database byte size and SHA-256. `publication.json` also records `MasterDataSha256`.

Recalculate SHA-256 on `mastermemory.db` and compare it with these values when checking local corruption.
