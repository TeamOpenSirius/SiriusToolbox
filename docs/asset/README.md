# Sirius.AssetTool

`Sirius.AssetTool` is a .NET 10 command-line application for local chart and episode resource processing.

```text
Sirius.AssetTool chart   SUS conversion and chart ENC processing
Sirius.AssetTool episode Episode scene JSON/BIN processing
```

## Build

From the repository root:

```powershell
dotnet build .\SiriusTools.sln -c Release
```

Run the tool through the project:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- --help
```

Set `SIRIUS_ASSET_TOOL_DEBUG=1` to print full exception details when a command fails.

## Chart commands

Show chart help:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- chart --help
```

### `chart text`

Convert SUS directly to Sirius plaintext:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- chart text input.sus output.txt
```

The output format is one comma-separated row per note or timing event:

```text
startTime,endTime,noteType,leftLane,laneLength,gimmickType,gimmickValue
```

Point events use `-1` as `endTime`.

### `chart encode`

Convert SUS and write an ENC file:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- chart encode input.sus output.enc --key "<32-byte-key>"
```

Optionally keep the intermediate plaintext:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- chart encode input.sus output.enc `
  --key "<32-byte-key>" `
  --text-out output.txt
```

The chart key must encode to exactly 32 UTF-8 bytes. Passing `--key` explicitly is recommended when the target environment uses a specific key.

ENC encoding is implemented as:

1. UTF-8 encode the Sirius plaintext without a BOM.
2. Compress it with Brotli using `CompressionLevel.Optimal`.
3. Derive a deterministic 16-byte IV with PBKDF2-SHA256 using the compressed bytes as the PBKDF2 input, an all-zero 8-byte salt, and 1,000 iterations.
4. Encrypt the compressed data with AES-256-CBC and PKCS#7 padding.
5. Prefix the ciphertext with the 16-byte IV.

### `chart decode`

Decode an ENC file to plaintext:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- chart decode input.enc output.txt --key "<32-byte-key>"
```

The decoder validates the ENC block length, decrypts AES-CBC, decompresses Brotli, and requires valid UTF-8 output.

### `chart selftest`

Run the built-in deterministic crypto round-trip test:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- chart selftest
```

The test verifies that repeated encoding of the same input with the same key produces identical bytes and that decoding restores the source text.

### Chart options

| Option | Behavior |
| --- | --- |
| `--key <value>` | Select the 32-byte UTF-8 chart key. |
| `--text-out <path>` | Write the intermediate Sirius plaintext while encoding. |
| `--ignore-wave-offset` | Do not add SUS `#WAVEOFFSET` to generated timestamps. |
| `--strict` | Convert parser warnings into errors before conversion. |

The converter supports tempo mapping, 12 game lanes, point notes, slide/hold generation, scratch/flick handling, generated hold ticks, `#TIL00` timing events, and `#TIL01` split-line events. Invalid lane ranges, malformed slide state, invalid BPM values, and other structurally inconsistent input are rejected.

See [Chart Validation](chart-validation.md) for validation behavior.

## Episode commands

Show episode help:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- episode --help
```

Episode scene binaries contain only the serialized `EpisodeDetail` array. Wrapper metadata such as `EpisodeId`, `StoryType`, `Order`, `Prev`, `Next`, `Chapter`, and `Title` is not stored in the BIN payload.

### Accepted JSON input

`pack` accepts either a wrapper object:

```json
{
  "EpisodeId": 1001,
  "Title": "Example",
  "EpisodeDetail": []
}
```

or a top-level `EpisodeDetail` array:

```json
[]
```

JSON parsing is case-insensitive, permits trailing commas and comments, and accepts numeric values encoded as strings. Each detail record must contain `CharacterMotions`. When a non-zero wrapper `EpisodeId` is present, every detail must use the same `EpisodeMasterId`.

### `episode pack`

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- episode pack episode.json -o scene.bin
```

If `-o`/`--output` is omitted, the output uses the input path with a `.bin` extension.

Before publishing the file, the tool serializes `EpisodeDetail[]` using MessagePack with `Lz4BlockArray`, deserializes the result, and checks key record fields and character-motion counts.

### `episode pack-dir`

Recursively pack every `*.json` file below a directory:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- episode pack-dir episode-json -o scene-bin
```

The relative directory structure is preserved and file extensions are changed to `.bin`. The command returns exit code `2` if one or more files fail while continuing to process the remaining files.

### `episode unpack`

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- episode unpack scene.bin -o episode.json
```

The generated JSON contains:

```json
{
  "EpisodeId": 1001,
  "EpisodeDetail": []
}
```

`EpisodeId` is taken from the first detail record, or `0` for an empty array.

### `episode inspect`

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- episode inspect scene.bin
```

`inspect` reports file size, counts UTF-8 replacement-byte sequences (`EF BF BD`), attempts MessagePack deserialization, and prints basic record information when successful. It returns exit code `2` when the binary appears text-corrupted or cannot be deserialized.

### Overwrite behavior

`pack`, `pack-dir`, and `unpack` refuse to replace an existing output file unless `-f` or `--force` is supplied.
