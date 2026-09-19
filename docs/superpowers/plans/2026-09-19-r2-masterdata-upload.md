# R2/CDN 全量同步迁移实施计划

> 本计划按 TDD 执行：先让迁移测试失败，再完整迁移服务、CLI、GUI，完成旧版对照验证后再删除旧仓库的重复 R2 路径并同步旧仓库索引。

## 任务

- [x] 对照旧版 `R2AssetPublisher`、`R2ObjectStoreClient` 和 `R2FileHashCache`，确认 MasterData、catalog、CDN 文件、scenes、notations、重试和缓存行为。
- [x] 在 `Sirius.AssetTool/R2` 实现完整对象发现、旧版路径映射、内容类型、预览映射清单和 5 GiB/重复键/工作文件校验。
- [x] 迁移 SHA-256 本地缓存、CDN manifest 播种、远端 HEAD 比较、并发上传、失败重试、force、取消和进度统计。
- [x] 增加 `r2 sync`、`r2 cdn` 和 `r2 masterdata` 中文 CLI；保留旧式 `--r2-sync`、`--r2-*` 参数名兼容入口。
- [x] 增加中文合并版 `R2SyncForm`，将 MasterData 纳入全量同步，接入首页、窗口生命周期和 UI 自检；保留 `r2 masterdata` CLI 兼容入口。
- [x] 更新 README、AssetTool/ToolboxUI/架构/验证文档和 smoke test。
- [x] 在旧仓库完整对照验证通过后，移除旧 R2 专用源文件、选项与调用分支，并同步 CodeGraph；保留无关下载/镜像/索引能力。
- [x] 运行 Toolbox Debug/Release 构建、服务 harness、CLI 自检、UI 自检、smoke test、单文件发布、旧仓库回归和最终 diff 检查。

## 关键约束

- 不打印、不提交凭据；不把真实密钥写入脚本或文档。
- 不重置当前 Toolbox 脏工作区；只添加本功能所需修改。
- 不删除旧 `Sirius.AssetTool` 的非 R2 功能。
- 旧仓库外部写入已在完整迁移和对照验证后执行，并运行 `codegraph sync`；旧 R2 源码已从旧入口移除。
