# SiriusTools

SiriusTools is a .NET command-line toolkit for working with World Dai Star data and local game assets. The repository contains two executable tools and one shared library.

- **Sirius.MasterTool** retrieves the current MasterData manifest, downloads `mastermemory.db`, tracks synchronization state, and can export MasterMemory tables to JSON.
- **Sirius.AssetTool** converts SUS charts to the Sirius chart format, encodes and decodes chart ENC files, and packs or unpacks episode scene binaries.
- **Sirius.Tooling.Core** contains the protocol contracts, MessagePack codecs, MasterMemory export logic, episode codecs, schema data, and shared file utilities used by the command-line tools.

## Requirements

- .NET 10 SDK
- Network access for `Sirius.MasterTool`

Check the installed SDK with:

```powershell
dotnet --info
```

## Build

Build the complete solution:

```powershell
dotnet restore .\SiriusTools.sln
dotnet build .\SiriusTools.sln -c Release
```

A repository smoke test is also available:

```powershell
.\scripts\smoke-test.ps1
```

The script restores and builds the solution, then runs the non-network help commands for both tools.

## Quick start

### Synchronize MasterData

```powershell
dotnet run --project .\src\Sirius.MasterTool\Sirius.MasterTool.csproj -- --sync --dir output
```

The default output contains the downloaded MasterMemory database, local manifests and state, and per-table JSON when JSON export is enabled.

See [MasterTool documentation](docs/master/README.md).

### Convert a SUS chart

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- chart text chart.sus chart.txt
```

Create an ENC file:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- chart encode chart.sus chart.enc --key "<32-byte-key>"
```

### Pack an episode scene

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- episode pack episode.json -o scene.bin
```

See [AssetTool documentation](docs/asset/README.md).

## Repository layout

```text
SiriusTools.sln
Directory.Build.props
Directory.Packages.props

src/
  Sirius.Tooling.Core/
    Episodes/              Episode protocol models and codecs
    IO/                    Shared file utilities
    Master/                MasterData protocols, export, persistence, serialization
    Schema/table.json      MasterMemory schema used for named JSON fields

  Sirius.MasterTool/
    Cli/                   Command-line parsing
    Configuration/         Synchronization options
    Networking/            Official API client and resumable download logic

  Sirius.AssetTool/
    Charts/                SUS parsing, conversion, chart format and ENC codec
    Episodes/              Episode commands and binary diagnostics

docs/
  master/                  MasterTool reference
  asset/                   AssetTool reference
```

## Project dependencies

```text
Sirius.MasterTool ─┐
                   ├──> Sirius.Tooling.Core
Sirius.AssetTool  ─┘
```

`Sirius.Tooling.Core` does not depend on either executable project, and the two executable projects do not reference each other.

## Build configuration

Common project settings are defined in `Directory.Build.props`:

- target framework: `net10.0`;
- nullable reference types enabled;
- implicit global usings enabled;
- warnings treated as errors;
- Release optimization, server GC, tiered compilation, and tiered PGO enabled.

NuGet package versions are managed centrally through `Directory.Packages.props`. `MessagePack` is consumed by `Sirius.Tooling.Core`.

The canonical MasterMemory schema is `src/Sirius.Tooling.Core/Schema/table.json`. `Sirius.MasterTool` links that file into its build output as `data/table.json`.

## Architecture

For project boundaries and data flows, see [Repository Architecture](docs/architecture.md).
