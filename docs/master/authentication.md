# Authentication and State

`Sirius.MasterTool` authenticates before requesting the MasterData manifest. Credentials can be supplied directly, loaded from environment variables, or reused from `state.json`.

## API sequence

The synchronization workflow uses these requests:

```text
POST /api/Environment
POST /api/Account/Register       when no LoginToken is available
POST /api/Account/Authenticate   when no usable access token is available
POST /api/Login
GET  /api/data/master
```

The API endpoint used after Environment is taken from `EnvironmentResult.ApiEndpoint` when provided; otherwise the bootstrap URL remains in use.

## Token sources

LoginToken sources, in effective priority order:

1. `--login-token`;
2. `WDS_ACCOUNT_TOKEN`;
3. `state.json`.

Access-token sources:

1. `--access-token`;
2. `WDS_AUTH_TOKEN`;
3. `state.json`, but only when the saved authentication client parameters match the current run.

## Automatic registration

If neither an access token nor a LoginToken is available, the tool calls `/api/Account/Register` using the value of `--register-name`. The returned LoginToken is saved immediately before authentication continues.

## Authentication client parameters

The Authenticate request includes:

- LoginToken;
- game version;
- authentication application version;
- APK hash values expected by the client protocol;
- APK application signature.

The authentication application version is the direct concatenation of `ApplicationVersion` and `AuthenticationVersionSuffix`.

## Cached access-token validation

A saved access token is reused only when both of these saved values match the current run:

- `GameVersion`;
- `AuthenticationApplicationVersion`.

If either differs, the saved access token is ignored and the tool authenticates again when a LoginToken is available.

## HTTP 440 handling

If `/api/Login` returns HTTP 440 and a LoginToken is available, the tool performs one re-authentication attempt, saves the replacement access token, and retries login once.

A failure after that retry is returned to the caller.

## Request headers

The API client sends:

- `User-Agent: BestHTTP/2 v2.8.5`;
- `Accept: application/vnd.msgpack`;
- `X-Platform`;
- `X-FM`;
- `X-Game-Version`;
- `X-Client-Version`;
- `X-Asset-Version` after Environment has been loaded;
- `X-MasterData-Version` after the MasterData manifest has been loaded;
- `Authorization: Bearer <token>` for authenticated requests.

MessagePack request bodies use `application/vnd.msgpack`.

## `state.json`

The state file contains synchronization and authentication data, including:

- LoginToken and access token;
- resolved API and content endpoints;
- asset and MasterData versions;
- MasterData JSON export version;
- application/authentication version values;
- game version;
- update timestamp.

Because the file contains credentials, treat it as private runtime state and do not publish it.

## TLS validation

TLS certificate validation is enabled by default.

`--insecure` installs an unconditional certificate-validation callback for the tool's HTTP client. Use it only when debugging a network environment where disabling validation is intentional.
