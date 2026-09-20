# Official CDN asset mirror

## English

The official sync window (`Sirius.ToolboxUI` → 官方 MasterData / CDN 同步) mirrors the
complete official asset set into one output root. The **CDN 资源** tab configures the
asset side; the **账号与主数据** tab handles registration, authentication, and the
`mastermemory.db` download that the asset side depends on.

### What is mirrored

| Source | Rule |
| --- | --- |
| Addressables catalogs | `{AssetUrl}/{category}/{platform}/{version}/catalog_{version}.json.br`, then `.json`; Android also tries the legacy shapes. `CatalogTemplate` overrides the shapes with `{base}`, `{category}`, `{version}`, `{platform}` placeholders. |
| Catalog objects | Every `m_InternalIds` entry, after expanding `m_InternalIdPrefixes` and the `{AssetUrl}` / `{AssetVersion}` / `{AssetType}` / `{AssetPlatform}` placeholders. |
| iOS objects | When the iOS catalog hash equals the Android hash, iOS object URLs are still derived by swapping the `/Android/` segment, because identical catalogs do not prove identical blobs. |
| static-assets | Derived from `BannerMaster`, `GachaMaster`, information and comic master rows, already cached files, and the `<output root>/static-assets.txt` seed file. |
| notations | Derived from `LiveMaster`, `AnotherNotationMaster`, and `DugongRunCourseMaster`, plus each notation directory's `music_config.enc`. |
| Scenes | One official episode-detail call per episode; the resolved source is cached at `indexes/episodes_<masterDataVersion>.json`. |

Default categories are `2d-assets`, `3d-assets`, and `cri-assets`. Skips are explicit:
`跳过 static-assets`, `跳过剧集场景`, `仅更新 catalog 与清单`, and `强制重下全部资源对象`.

### Output layout

```text
<output root>
  master/mastermemory.db
  master/manifest.json
  master/json/...
  state.json
  publication.json
  catalogs/<category>/catalog_<version>.json[.br|.hash]
  files/<category>/<origin host>/<path>
  files/static-assets/<path>
  files/notations/<path>
  files/scenes/<episode>.bin
  indexes/episodes_<masterDataVersion>.json
  manifests/cdn_<assetVersion>.json
```

### Incremental behaviour

- The remote `<catalog>.hash` is compared with the stored hash. An unchanged hash reuses
  the cached catalog instead of re-downloading it.
- Each catalog run reports `新增 / 未变 / 移除`; objects that disappeared from the catalog
  are dropped from the manifest and counted in the result's `RemovedCount`.
- Objects whose manifest status is `Complete` and whose local file still exists are not
  downloaded again. `强制重下全部资源对象` overrides this.
- Existing `manifests/cdn_*.json` files are imported first, so a version bump keeps
  previously completed objects complete when the relative path and file still match.
- A partial download is kept as `<file>.part`. The next run sends `Range: bytes=<n>-` and
  appends the remainder. A `200` answer to a ranged request restarts from zero; a `416`
  answer whose total length equals the local `.part` length promotes the partial file to
  the final object. Episode scenes always restart from zero because their response may be
  content-encoded.
- `404` for notations or static-assets is recorded as `NotFound`, reported with `MISS`,
  and does not fail the run. A `404` for a catalog object is a failure, and the run ends
  with an error listing every failed object.

### Notes

- Every download is SHA-256 hashed before it is recorded. A later run verifies the hash of
  an already complete file against the manifest and re-downloads on mismatch.
- Published roots are the same layout the publication window consumes, so the official
  sync output can be pushed to Cloudflare R2 without a second discovery pass.

## 中文

官方同步窗口（`Sirius.ToolboxUI` → 官方 MasterData / CDN 同步）把完整的官方资源集增量镜像到
同一个输出根目录。**CDN 资源** 页配置资源侧参数；**账号与主数据** 页负责注册、认证以及资源侧
依赖的 `mastermemory.db` 下载。

### 镜像内容

| 来源 | 规则 |
| --- | --- |
| Addressables catalog | `{AssetUrl}/{category}/{platform}/{version}/catalog_{version}.json.br`，其次 `.json`；Android 额外尝试历史形态。`CatalogTemplate` 可用 `{base}`、`{category}`、`{version}`、`{platform}` 占位符覆盖地址形态。 |
| catalog 对象 | 展开 `m_InternalIdPrefixes` 与 `{AssetUrl}` / `{AssetVersion}` / `{AssetType}` / `{AssetPlatform}` 占位符后的全部 `m_InternalIds`。 |
| iOS 对象 | iOS catalog 哈希与 Android 相同时仍按平台池生成 iOS 地址（把 `/Android/` 段替换为 `/iOS/`），因为 catalog 相同并不代表二进制相同。 |
| static-assets | 由 `BannerMaster`、`GachaMaster`、公告与漫画主数据、既有缓存文件以及所选目录下的 `static-assets.txt` 种子文件推导。 |
| notations | 由 `LiveMaster`、`AnotherNotationMaster`、`DugongRunCourseMaster` 推导，并补齐各目录的 `music_config.enc`。 |
| 剧集场景 | 逐剧集调用官方详情接口；结果缓存在 `indexes/episodes_<masterDataVersion>.json`。 |

默认分类为 `2d-assets`、`3d-assets`、`cri-assets`。跳过行为必须显式开启：
`跳过 static-assets`、`跳过剧集场景`、`仅更新 catalog 与清单`、`强制重下全部资源对象`。

### 增量行为

- 先比对远端 `<catalog>.hash`；未变化时直接复用本地 catalog，不重复下载。
- 每次 catalog 同步都会报告 `新增 / 未变 / 移除`；从 catalog 消失的对象会从清单移除并计入
  结果的 `RemovedCount`。
- 清单状态为 `Complete` 且本地文件仍存在的对象不会重新下载；`强制重下全部资源对象` 可覆盖。
- 会先导入既有的 `manifests/cdn_*.json`，因此版本号变化后，相对路径与文件仍匹配的
  已完成对象保持完成状态。
- 未完成的下载保留为 `<file>.part`，下次发送 `Range: bytes=<n>-` 续传。范围内请求若返回 `200`
  则从零重下；若返回 `416` 且总长度等于本地 `.part` 长度，则直接把分片提升为最终文件。
  剧集场景因响应可能带 content encoding，始终从零开始。
- notations 与 static-assets 的 `404` 记为 `NotFound`、以 `MISS` 报告，不会让整次运行失败；
  catalog 对象的 `404` 属于失败，运行结束时会抛出并列出全部失败对象。

### 说明

- 每个下载对象在写入清单前都会计算 SHA-256；后续运行会校验已完成文件的哈希，不匹配则重新下载。
- 官方同步不会在所选目录下再创建 `assets` 子目录；若准备继续用 R2 发布窗口，建议直接选择名为
  `assets` 的目录作为官方同步输出目录，然后在 R2 窗口选择该目录。
