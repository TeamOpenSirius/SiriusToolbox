# Repository Architecture

SiriusTools is organized as a single .NET solution with two command-line applications and one shared library.

## Dependency graph

```text
                    ┌─────────────────────┐
                    │ Sirius.MasterTool   │
                    └──────────┬──────────┘
                               │
                               ▼
                    ┌─────────────────────┐
                    │ Sirius.Tooling.Core │
                    └─────────────────────┘
                               ▲
                               │
                    ┌──────────┴──────────┐
                    │ Sirius.AssetTool    │
                    └─────────────────────┘
```

The dependency direction is intentionally one-way. Shared protocol and codec code belongs in `Sirius.Tooling.Core`; command-line orchestration remains in the executable projects.

## Sirius.Tooling.Core

`Sirius.Tooling.Core` contains reusable format and persistence code without owning a command-line entry point or HTTP synchronization workflow.

### Master

`Master/` provides:

- MessagePack protocol DTOs for Environment, account authentication, login, and the MasterData manifest;
- request and response MessagePack serialization;
- MasterMemory table parsing and schema-based JSON mapping;
- synchronization state persistence in `state.json`;
- publication metadata persistence in `publication.json`.

### Episodes

`Episodes/` provides:

- MessagePack wire contracts for episode scene records;
- JSON wrapper parsing;
- LZ4BlockArray MessagePack serialization and deserialization;
- round-trip verification;
- JSON output for unpacked scene records.

### IO

`IO/AtomicFile.cs` provides atomic-style replacement for generated text and binary files.

### Schema

`Schema/table.json` describes class fields and enum values used when converting positional MasterMemory MessagePack data into readable JSON objects.

## Sirius.MasterTool

`Sirius.MasterTool` owns network synchronization and command-line configuration.

Its main flow is:

```text
CLI options
  -> fetch Environment
  -> resolve API endpoint and asset version
  -> load/reuse/register account credentials
  -> authenticate
  -> login
  -> fetch /api/data/master
  -> compare local MasterData version
  -> download/resume mastermemory.db when required
  -> write local manifest and state
  -> export tables to JSON when enabled
  -> write publication.json
```

Network behavior is concentrated in `Networking/SiriusApiClient.cs`; shared wire models and serializers come from `Sirius.Tooling.Core`.

## Sirius.AssetTool

`Sirius.AssetTool` is entirely local and exposes two command groups.

### Chart pipeline

```text
SUS file
  -> SusParser
  -> TempoMap
  -> SusToSiriusConverter
  -> Sirius plaintext rows
  -> Brotli compression
  -> AES-256-CBC
  -> ENC file
```

The decode path performs the inverse cryptographic and compression operations and writes UTF-8 plaintext.

### Episode pipeline

```text
JSON wrapper or EpisodeDetail[]
  -> validation
  -> EpisodeDetail[]
  -> MessagePack + LZ4BlockArray
  -> scene BIN
```

Unpacking deserializes the binary array and writes a JSON object containing `EpisodeId` and `EpisodeDetail`.

## Repository-wide configuration

`Directory.Build.props` defines the common .NET 10 compiler and Release settings. `Directory.Packages.props` enables central package version management.

Only `Sirius.Tooling.Core` directly references `MessagePack`; the executable projects access MessagePack-based behavior through the shared library.

## Boundary rules

When adding code, keep these boundaries:

1. Network calls, CLI parsing, and synchronization policy belong to `Sirius.MasterTool`.
2. SUS parsing and chart-specific conversion belong to `Sirius.AssetTool`.
3. Episode command presentation and file diagnostics belong to `Sirius.AssetTool`.
4. Shared wire contracts, codecs, schema mapping, and reusable persistence belong to `Sirius.Tooling.Core`.
5. `Sirius.Tooling.Core` must not reference either executable project.
6. `Sirius.MasterTool` and `Sirius.AssetTool` must not reference each other.
