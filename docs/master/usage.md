# Sirius.MasterTool Usage

## Requirements

`Sirius.MasterTool` targets `net10.0` and requires the .NET 10 SDK.

```powershell
dotnet --info
```

## Build

```powershell
dotnet build .\SiriusTools.sln -c Release
```

Run directly from the project:

```powershell
dotnet run --project .\src\Sirius.MasterTool\Sirius.MasterTool.csproj -- --sync [options]
```

A Windows Release build is normally produced under:

```text
src/Sirius.MasterTool/bin/Release/net10.0/
```

## Basic synchronization

```powershell
Sirius.MasterTool.exe --sync --dir output
```

`--sync` is required. Running without arguments prints help; supplying options without `--sync` produces an error.

## Options

| Option | Default | Description |
| --- | --- | --- |
| `--sync` | — | Run the MasterData synchronization workflow. |
| `--dir <path>` | `output` | Root output directory. |
| `--api <url>` | `https://api.wds-stellarium.com` | Bootstrap API used for the Environment request. |
| `--login-token <token>` | — | LoginToken to use instead of registering an account. |
| `--access-token <token>` | — | Existing Bearer access token. |
| `--register-name <name>` | `ArchiveUser` | Account name used for automatic registration. |
| `--app-version <version>` | `2.30.1` | Application version used by Environment and authentication. |
| `--auth-version-suffix <text>` | `.486` | Suffix appended to `--app-version` for the authentication client version. |
| `--game-version <number>` | `2` | Game protocol version. |
| `--platform <name>` | `google-play` | Value of the `X-Platform` header. |
| `--fm <value>` | `0` | Value of the `X-FM` header. |
| `--table-schema <path>` | build-provided schema | Override the MasterMemory `table.json` path. |
| `--force` | false | Redownload and re-export even when the local version marker matches. |
| `--no-json` | false | Skip MasterMemory JSON export. |
| `--insecure` | false | Disable TLS certificate validation. Intended only for controlled debugging. |
| `-h`, `--help` | — | Print command help. |

The authentication application version is constructed as:

```text
<app-version><auth-version-suffix>
```

With the defaults, the value is `2.30.1.486`.

## Authentication environment variables

| Variable | Purpose |
| --- | --- |
| `WDS_ACCOUNT_TOKEN` | Initial LoginToken value. |
| `WDS_AUTH_TOKEN` | Initial access-token value. |

A command-line token option overrides the corresponding environment-variable value. Saved state may be used when neither is provided.

## Examples

Download MasterMemory without exporting JSON:

```powershell
Sirius.MasterTool.exe --sync --dir output --no-json
```

Force a complete refresh:

```powershell
Sirius.MasterTool.exe --sync --dir output --force
```

Use a LoginToken:

```powershell
Sirius.MasterTool.exe --sync --login-token "<token>"
```

Use an access token directly:

```powershell
Sirius.MasterTool.exe --sync --access-token "<token>"
```

Select client parameters explicitly:

```powershell
Sirius.MasterTool.exe --sync `
  --app-version 2.30.1 `
  --auth-version-suffix .486 `
  --game-version 2 `
  --platform google-play
```

Use a custom schema:

```powershell
Sirius.MasterTool.exe --sync --table-schema D:\wds\table.json
```

## Incremental behavior

The database download is skipped when all of the following are true:

- `--force` is not set;
- the local MasterData version matches the remote manifest version;
- `master/mastermemory.db` exists.

JSON export has its own completion marker. When the marker matches the current export format and MasterData version, parsing is skipped unless `--force` is set.

## Exit codes

- `0`: completed successfully or help was displayed;
- `1`: unhandled error;
- `130`: cancelled with `Ctrl+C`.
