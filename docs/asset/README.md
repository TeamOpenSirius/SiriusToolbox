# Sirius.AssetTool

`Sirius.AssetTool` is a .NET 10 command-line application for local chart and episode resource processing.

```text
Sirius.AssetTool chart   SUS conversion and chart ENC processing
Sirius.AssetTool episode Episode scene JSON/BIN processing
Sirius.AssetTool r2      Cloudflare R2 MasterData/CDN full sync
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

## R2 主数据上传

上传命令只处理主数据发布目录中的 `master/mastermemory.db`。它读取同目录的 `master/manifest.json`，将 `Uri` 映射为 `master-data/production/...` 对象键，并把 SHA-256 写入 `x-amz-meta-sha256`。没有 `--force` 时，如果远端长度和 SHA-256 都一致则跳过上传。

先使用预览模式检查映射；预览模式不会读取文件哈希，也不会发出网络请求：

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- r2 masterdata .\output --dry-run
```

实际上传使用 R2 API 令牌对应的 S3 访问密钥。建议通过环境变量提供凭据，避免把秘密写入命令历史：

```powershell
$env:R2_ACCESS_KEY_ID = '<access-key-id>'
$env:R2_SECRET_ACCESS_KEY = '<secret-access-key>'
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- r2 masterdata .\output `
  --endpoint 'https://<account-id>.r2.cloudflarestorage.com' `
  --bucket '<bucket>'
```

支持 `--prefix`、`--retries`、`--force`、`--session-token`；CLI 也兼容旧式 `--r2-sync --dir <输出目录>` 以及 `--r2-endpoint`、`--r2-bucket`、`--r2-prefix`、`--r2-retries`、`--r2-force` 和 `--r2-dry-run`。Endpoint、区域和 PUT 行为参照 [Cloudflare R2 S3 API](https://developers.cloudflare.com/r2/api/s3/api/)。

### `r2 sync`

完整同步会按旧 AssetTool 的规则发现并上传：

- `master/manifest.json` + `master/mastermemory.db` → `master-data/production/...`；
- `assets/catalogs/<category>/<platform>/catalog_<version>.*` → `production/<category>/<platform>/<version>/...`；
- `assets/files/<category>/<origin-host>/<path>` → `production/<path>`；
- `assets/files/scenes/...` → `master-data/production/scenes/...`；
- `assets/files/notations/...` → `production/Notations/...`。

先生成完整映射：

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- r2 sync .\output --dry-run
```

正式同步：

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- r2 sync .\output --concurrency 16 --retries 5
```

正式同步会使用 `assets/r2-hash-cache.json`，优先复用文件长度/修改时间仍匹配的 SHA-256，并通过远端 `HEAD` 的大小和 `x-amz-meta-sha256` 跳过未变化对象。GUI 中的 “主数据 / CDN / R2 全量同步” 会在同一次操作中包含 MasterData，并提供相同选项和映射表。

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

### `episode unpack-dir`

Recursively unpack every `*.bin` file below a directory and preserve its relative structure:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- episode unpack-dir scene-bin -o episode-json-restored
```

The output extension changes to `.json`. If `-o` is omitted, the tool uses the input directory with a `-json` suffix. Each file is processed independently; the command returns exit code `2` if one or more files fail.

### `episode inspect`

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- episode inspect scene.bin
```

`inspect` reports file size, counts UTF-8 replacement-byte sequences (`EF BF BD`), attempts MessagePack deserialization, and prints basic record information when successful. It returns exit code `2` when the binary appears text-corrupted or cannot be deserialized.

### `episode cache`

Build a Version 2 `scene-assets.json`-compatible cache from an Episode JSON directory and a scene BIN directory:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- episode cache .\episode-json .\scene-bin -o .\scene-assets.json
```

The scan is recursive. BIN file names must be numeric IDs; matching JSON is found by wrapper `EpisodeId` or the first detail's `EpisodeMasterId`. Unmatched BIN files remain in the cache with an empty `SourcePath`, while duplicate numeric IDs fail the build before output is written. Use `--metadata-only` to record file size and timestamp without reading file contents, or provide `--master-data-version` and `--source-revision` for cache provenance.

The cache records `Version`, `MasterDataVersion`, `SourceRevision`, `GeneratedAt`, and per-asset `RelativePath`, `FileName`, `Sha256`, `GitObjectId`, `HashAlgorithm`, `SourcePath`, `MetadataOnly`, `FileSize`, and `LastWriteTimeUtcTicks`. Local generation leaves `GitObjectId` empty because it does not invoke Git.

### Overwrite behavior

`pack`, `pack-dir`, `unpack`, `unpack-dir`, and `cache` refuse to replace an existing output file unless `-f` or `--force` is supplied.
