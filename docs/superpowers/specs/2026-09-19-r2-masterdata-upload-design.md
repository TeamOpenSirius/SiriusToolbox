# R2/CDN 全量同步迁移设计

## 目标

把 `SiriusServer/Sirius.AssetTool` 中已经验证过的完整 R2/CDN 发布路径迁入 Toolbox，提供可复用服务、中文 CLI 和 ToolboxUI 子窗口。功能必须覆盖旧版的 MasterData、catalog、CDN 文件、scenes、notations、远端增量判断、并发上传、失败重试和本地哈希缓存；另外保留一个只上传 MasterData 的快捷入口。

## 范围裁决

- 完整迁移旧工具的 R2 发布实现：对象发现和路径映射、内容类型、预览映射、远端 HEAD 比较、SHA-256 缓存、CDN 清单播种、并发上传、重试、强制上传和取消；
- 迁移完成并通过旧版对照验证后，移除旧工具中重复的 R2 专用源文件、选项和入口；在验证完成前保留旧仓库副本，避免再次丢失回滚实现；
- 保留旧 `Sirius.AssetTool` 的官方下载、MasterData 打包、资产镜像和索引能力，因为这些不是本次主数据 R2 上传功能的一部分；
- ToolboxUI 首页只增加一个“主数据 / CDN / R2 全量同步”窗口；MasterData 自动作为全量同步对象之一，与 CLI 共用同一套 R2 服务和 SigV4 请求实现；
- 不在仓库中保存 R2 密钥；GUI 可从环境变量预填，日志永不输出秘密；
- GUI 默认使用预览模式，确认对象键后才允许实际请求；
- 预览不读取主数据内容、不计算哈希、不访问网络；实际上传使用 `x-amz-meta-sha256` 判断远端对象是否可跳过。

## 全量同步与缓存

全量同步扫描以下输入：

```text
<output>/master/manifest.json + mastermemory.db
<output>/assets/catalogs/**/catalog_<version>.json[.br|.hash]
<output>/assets/files/**/*
<output>/assets/manifests/cdn_*.json  （仅用于播种本地哈希缓存）
```

映射规则与旧版一致：catalog 使用 `production/<category>/<platform>/<version>/...`；带源站域名的 CDN 镜像去除本地类别和域名段；scenes 映射到 `master-data/production/scenes/...`；notations 映射到 `production/Notations/...`；其余资源位于 `production/...`。`.bck`、`.part`、`.tmp` 不参与发布，重复对象键和超过 5 GiB 的文件拒绝。

正式同步维护 `<output>/assets/r2-hash-cache.json`，按相对路径、文件长度和 UTC 修改时间复用 SHA-256，并从已完成的 `cdn_*.json` 清单补充有效哈希。远端对象只有在长度、`x-amz-meta-sha256` 和 Content-Encoding 都一致时才跳过；预览只生成 `<output>/assets/r2-object-map.tsv`，不计算哈希、不写缓存、不访问网络。

## 输入与映射

```text
<output>/master/manifest.json
<output>/master/mastermemory.db
        │
        └─ manifest.Uri = master-data/production/<version>/mastermemory.db
        └─ R2 PUT object key（可选前缀 + 上述路径）
```

清单必须同时存在 `Uri` 字符串和数据库文件；绝对 URI、`.`、`..` 路径段以及超过 5 GiB 的对象均拒绝。默认 Content-Type 为 `application/octet-stream`。

## R2 协议

R2 使用 S3 兼容 endpoint、`auto` 区域和 AWS SigV4。实现沿用旧工具的手工签名方式，不引入 AWS SDK 的流式 payload 签名路径；HEAD 比较长度和 `x-amz-meta-sha256`，PUT 设置 SHA-256 元数据、公共缓存一年，并对可重试的网络/服务端错误指数退避。

## 界面与 CLI

- GUI 字段：本地目录、endpoint、bucket、对象前缀、访问密钥、秘密访问密钥、临时令牌、重试次数、强制上传、预览模式；
- CLI：`r2 sync <output-dir> ...`（另有 `r2 cdn` 别名）和 `r2 masterdata <output-dir> ...`；支持 endpoint、bucket、prefix、并发、重试、force、dry-run、凭据选项；凭据优先从 `R2_ACCESS_KEY_ID`、`R2_SECRET_ACCESS_KEY`、`R2_SESSION_TOKEN` 读取；迁移脚本继续接受旧式 `--r2-sync` 及 `--r2-*` 参数名；
- 入口生命周期沿用 ToolboxUI：打开子窗口隐藏首页，关闭子窗口恢复首页，重复打开只激活现有窗口；
- 所有可见 GUI 文案使用中文，协议字段和命令参数保留原样。

## 验收

- 服务测试覆盖完整对象键映射、中文/嵌套路径、预览无网络、哈希缓存复用、CDN 清单播种、绝对 URI/路径穿越、缺失文件和凭据校验；
- CLI `r2 --help`、`r2 sync --help`、旧式参数兼容、预览命令和完整 AssetTool 回归通过；
- ToolboxUI 自检构造两个 R2 窗口无异常；
- Debug/Release solution build、smoke test 和单文件发布通过；
- 旧仓库在完成对照验证后不再存在重复 R2 publisher/client/cache 的源文件、选项或调用分支。
