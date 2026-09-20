# Output layout

```text
output/
  state.json                   private authentication/download state
  master/
    mastermemory.db            verified downloaded database
    mastermemory.db.bck        previous database when refreshed
    mastermemory.db.part       resumable temporary download
    manifest.json              version, URI, timestamp, size and SHA-256
    json/<TableName>.json      optional typed export
  assets/                      scene/catalog/CDN files and R2 cache/map files
```

# 输出布局

`state.json` 是私有认证状态；`master` 保存校验后的数据库、备份、断点下载临时文件、
清单和可选 JSON。官方同步把 `catalogs`、`files`、`manifests`、`indexes` 直接写入所选目录，
不再额外创建 `assets` 子目录；R2 发布窗口仍兼容项目根目录下的既有 `assets` 布局。
