# Repository Architecture

## English

```text
SiriusData
  Sirius.Protocol       shared DTOs and the only MasterMemory table models
  Sirius.MasterData     typed edit/export/rebuild/validation library

SiriusToolbox
  Sirius.Toolbox        Chart, Episode, scene index, official sync, CDN/R2 services
  Sirius.ToolboxUI      WinForms and the only executable entry point
```

`Sirius.ToolboxUI` calls both libraries in-process and never starts a child CLI.
Official synchronization downloads `mastermemory.db`, validates it with
`Sirius.MasterData`, writes local manifest/state files, and optionally exports JSON.
It then mirrors the official CDN asset set incrementally (catalogs, catalog objects,
static-assets, notations, and episode scenes) into `assets/` and records the result in
`assets/manifests/cdn_<version>.json`.
R2 publication scans the resulting MasterData and asset directories and performs
signed S3-compatible HEAD/PUT requests.

The server only consumes produced `mastermemory.db` and scene-index JSON files. It does
not build either artifact.

## 中文

`Sirius.Protocol` 是 MasterMemory 表模型唯一来源；`Sirius.MasterData` 提供编辑、
导出、重建与校验。Toolbox 仓库中的 `Sirius.Toolbox` 放全部业务实现，
`Sirius.ToolboxUI` 是唯一可执行入口，不启动或保留子 CLI。

官方下载流程取得数据库后通过共享模型校验，再写入清单、状态并按需导出 JSON；
随后增量镜像官方 CDN 资源集（catalog、catalog 对象、static-assets、notations 与剧集场景）
到 `assets/`，并把结果记入 `assets/manifests/cdn_<版本>.json`。R2 发布流程扫描这些产物和
资源目录。服务端只消费数据库与场景索引，不负责建立它们。
