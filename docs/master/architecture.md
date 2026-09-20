# MasterData architecture

`Sirius.Toolbox.Master.Sync.MasterDataSyncService` owns official synchronization.
`Sirius.MasterData.MasterMemoryDatabaseService` owns typed table access, editing,
rebuild, validation, JSON export, and localization. Both reuse `Sirius.Protocol` models.
The UI only collects parameters and displays progress.

# 主数据架构

官方下载策略在 `MasterDataSyncService`，类型化访问、编辑、重建、校验和导出在同级
仓库的 `Sirius.MasterData`；两者复用 `Sirius.Protocol` 模型，UI 只收集参数并显示进度。
