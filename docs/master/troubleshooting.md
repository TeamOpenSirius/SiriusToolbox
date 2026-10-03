# MasterData troubleshooting

- For build failures, check access to the SiriusData latest Release assets and nuget.org; see ../rolling-packages.md.
- For authentication errors, verify the application version and tokens in the sync window.
- The editor rejects databases that cannot load through generated `MemoryDatabase` tables.
- If saving fails, close other programs using the file or use Save As.

# 主数据故障排查

构建失败时检查 GitHub Release 和 nuget.org 网络连接；业务认证失败时检查客户端版本与令牌；数据库无法通过生成的
`MemoryDatabase` 加载时会被拒绝；保存失败时关闭占用文件的程序或改用另存为。
