# ToolboxUI MasterData usage

1. Start `Sirius.ToolboxUI`.
2. Open “官方 MasterData 下载”, choose an output directory, and synchronize.
3. Open “主数据编辑器” and select `master/mastermemory.db`.
4. Browse schemas and records, edit JSON, then use Save or Save As.
5. Publish through “主数据 / CDN / R2 全量同步” after reviewing the dry-run mapping.

All writes are rebuilt atomically and reloaded through generated MasterMemory APIs.

# ToolboxUI 主数据用法

启动 ToolboxUI 后，先在“官方 MasterData 下载”选择输出目录并同步，再用“主数据编辑器”
打开 `master/mastermemory.db`。编辑后使用保存或另存为；发布前在 R2 窗口先预览映射。
