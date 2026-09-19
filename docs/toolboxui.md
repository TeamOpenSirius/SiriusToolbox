# Sirius.ToolboxUI

`Sirius.ToolboxUI` is the Chinese Windows Forms toolbox for MasterData, Chart, Episode, scene-cache, Episode-editor, and the combined MasterData/CDN/R2-sync workflow.
It uses the same typed MasterMemory implementation as the CLI through `Sirius.MasterData` and the generated models in `lib/Sirius.Protocol.dll`.

## Run

```powershell
dotnet run --project .\src\Sirius.ToolboxUI\Sirius.ToolboxUI.csproj
```

You can also pass a database path directly:

```powershell
dotnet run --project .\src\Sirius.ToolboxUI\Sirius.ToolboxUI.csproj -- .\mastermemory.db
```

The start window has six entries. Opening an entry hides the start window; closing the child window restores and activates the start window. Reopening an already open entry activates the existing child window.

The Chart window converts SUS text, encodes/decodes ENC files, and runs the chart self-test. The Episode window supports JSON/BIN single-file operations, directory batch packing, BIN-to-JSON directory batch unpacking, and BIN inspection. The 剧情资源缓存 window scans Episode JSON and scene BIN directories to build or inspect a Version 2 `scene-assets.json` cache. The 剧情编辑器 window edits common dialogue/resource fields, preserves unknown wrapper metadata and advanced protocol fields, saves readable Chinese JSON, and exports verified BIN files.

## 主数据 / CDN / R2 全量同步

合并后的同步窗口读取所选目录下的 `master/manifest.json`、`master/mastermemory.db`、`assets/catalogs` 和 `assets/files`，一次性预览或同步 MasterData、目录清单和 CDN 资源。MasterData 清单中的 `Uri` 会映射为 `master-data/production/...` 对象键，可通过对象键前缀调整发布路径。窗口默认勾选“仅预览，不访问 R2”，确认完整对象映射后再取消勾选并填写 R2 S3 地址、存储桶和凭据。

访问密钥也可以通过 `R2_ACCESS_KEY_ID`、`R2_SECRET_ACCESS_KEY` 和可选的 `R2_SESSION_TOKEN` 环境变量预填；秘密字段不会写入日志。非强制上传会先以 HEAD 请求检查远端 `Content-Length` 和 `x-amz-meta-sha256`，一致时跳过上传；网络失败会按设置重试。

无界面的等价命令（MasterData 会自动包含在全量同步中）：

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- r2 sync .\output --dry-run
```

实现依据 [Cloudflare R2 S3 API](https://developers.cloudflare.com/r2/api/s3/api/) 的 `PutObject` 兼容接口，使用 R2 要求的 `auto` 区域 SigV4 签名。

窗口迁移了旧 AssetTool 的完整发布流程：扫描 `master`、`assets/catalogs`、`assets/files`，按旧规则将 MasterData、源站目录和 scenes/notations 映射到 R2 对象键；支持并发上传、失败重试、强制上传、预览映射、远端长度与 `x-amz-meta-sha256` 比较，以及本地哈希缓存复用。

实际同步会维护：

- `assets/r2-hash-cache.json`：按本地文件长度和 UTC 修改时间复用 SHA-256，也会从 `assets/manifests/cdn_*.json` 中播种已完成资源的哈希；
- `assets/r2-object-map.tsv`：预览模式下写入完整的本地路径、对象键、大小、Content-Type 和 Content-Encoding 映射。

CLI 等价入口：

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- r2 sync .\output --dry-run
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- r2 sync .\output --concurrency 16 --retries 5
```

`r2 cdn` 是 `r2 sync` 的别名；旧式 `--r2-sync --dir <输出目录>`、`--r2-endpoint`、`--r2-bucket`、`--r2-prefix`、`--r2-concurrency`、`--r2-retries`、`--r2-force` 和 `--r2-dry-run` 参数仍可用于迁移脚本。

## 剧情资源缓存

Select the Episode JSON directory, scene BIN directory, and output cache path. Optional MasterData/source revision values are stored in the cache. “只记录文件元数据” leaves `Sha256` and `HashAlgorithm` empty and avoids reading BIN contents; otherwise the cache computes SHA-256. The generated table shows ID, relative BIN path, JSON source, size, hash, and mode. Duplicate numeric BIN IDs are rejected before writing output.

The same operation is available headlessly:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- episode cache .\episode-json .\scene-bin -o .\scene-assets.json --metadata-only
```

## 剧情编辑器

Open a wrapper JSON or a top-level `EpisodeDetail` array. The left list shows order, speaker, and phrase; the right side edits IDs/order, speaker/title/effect, background and audio resources, voice, and dialogue text. Apply changes before switching records, or choose whether to apply/discard pending edits when prompted. New, duplicate, and delete actions update the in-memory document; save/另存为 keeps unknown wrapper properties, and 导出 BIN uses the shared MessagePack/LZ4 codec plus a round-trip verification.

## Current UI features

- open a `mastermemory.db` file or drag one onto the window;
- table list with table-name filtering and row counts;
- paged record grid with generated schema columns;
- primary-key lookup, including composite keys;
- schema view with property type, MessagePack key, writability, and primary-key flags;
- JSON editor for the selected typed record;
- add a blank record or duplicate an existing record and edit its primary key;
- update and delete records;
- verify the complete database with the generated MasterMemory loader/validator;
- save or save-as with an automatic `.bak` of an overwritten destination.

## Editing model

Opening a database creates a temporary working copy. Add/update/delete operations modify only that working copy until **Save** or **Save As** is used. Closing with unsaved changes requires confirmation.

Every write goes through `MasterMemoryDatabaseService`. Only the changed table is rebuilt; the untouched table blocks are preserved from the source database. The rebuilt database is then loaded again through `Sirius.Protocol.Shared.MemoryDatabase`, and the generated validator is invoked when available.

The record editor accepts the same JSON shapes as the CLI `db add` and `db update` commands. For an existing record, ToolboxUI computes a top-level JSON delta and submits only changed properties, so unchanged read-only model properties are not written back. **Duplicate** keeps writable properties only; change the copied primary key before adding it. For nested objects and arrays, edit the JSON representation directly.

## Platform

The UI project targets `net10.0-windows` and uses WinForms. The CLI and asset projects remain ordinary `net10.0` applications.
