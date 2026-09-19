# Episode 资源缓存与简易剧情编辑器设计

## 目标

在现有 `Sirius.ToolboxUI` 中增加 Episode 资源缓存生成和简易剧情编辑能力。缓存生成结果兼容仓库中的 `scene-assets.json` Version 2 结构；剧情编辑器支持常见剧情字段编辑，并继续复用现有 Episode JSON/BIN 编解码逻辑。

## 已确认的范围

- 资源缓存从用户选择的 Episode JSON 目录和 scene BIN 目录建立，不依赖网络；
- 缓存文件默认使用 `scene-assets.json` 的 Version 2 顶层结构；
- GUI 文案继续使用中文，文件格式字段名和 CLI 参数保持原样；
- 保留现有 Episode 单文件、目录批量打包、目录批量 BIN→JSON 解包和检查功能；
- 新增独立的“剧情资源缓存”窗口和“剧情编辑器”窗口；主界面仍以工具入口中心管理子窗口；
- 编辑器面向常见对白和资源字段，不做完整的视觉小说预览器；
- 保存后可以调用现有 Episode 编码逻辑生成 BIN，不复制 MessagePack/LZ4 实现。

## `scene-assets.json` 数据模型

缓存文档包含：

```json
{
  "Version": 2,
  "MasterDataVersion": "",
  "SourceRevision": "",
  "GeneratedAt": "2026-09-19T00:00:00Z",
  "Assets": {
    "1001": {
      "EpisodeMasterId": 1001,
      "RelativePath": "scenes/1001.bin",
      "FileName": "1001.bin",
      "Sha256": "...",
      "GitObjectId": "",
      "HashAlgorithm": "sha256",
      "SourcePath": "episode/1001.json",
      "MetadataOnly": false,
      "FileSize": 2757,
      "LastWriteTimeUtcTicks": 638...
    }
  }
}
```

缓存服务只为实际发现的 `*.bin` 建立资产项，字典键使用 Episode ID 的十进制字符串。BIN 文件名必须是数字 ID（例如 `1001.bin`）；JSON 的 ID优先从包装对象的 `EpisodeId` 或首条 `EpisodeMasterId` 获取，无法获取时使用数字文件名。

目录可以递归扫描。相对路径使用 `/` 分隔，并默认加上 `episode/` 和 `scenes/` 前缀，以匹配参考缓存；目录前缀作为构建选项保留可配置能力。

默认生成 SHA-256、文件大小和 UTC 修改时间。勾选“仅元数据”时不读取文件内容，`Sha256` 为空、`HashAlgorithm` 为空、`LastWriteTimeUtcTicks` 为 `0`，但仍记录文件大小。工具无法凭本地文件推导参考缓存中的 Git blob ID，因此 `GitObjectId` 默认为空；`SourceRevision` 可由用户手工填写。

相同 Episode ID 出现多个 BIN 时构建失败并指出冲突路径，避免静默覆盖。没有匹配 JSON 的 BIN 仍可以建立缓存，但 `SourcePath` 为空；没有匹配 BIN 的 JSON 不会单独生成资产项。

## 公共服务边界

新增 `Sirius.AssetTool.Episodes.SceneAssetCacheService`：

```csharp
public sealed record SceneAssetCacheOptions(
    bool MetadataOnly,
    string? MasterDataVersion,
    string? SourceRevision,
    string EpisodePathPrefix = "episode",
    string ScenePathPrefix = "scenes");

public sealed record SceneAssetCacheEntry(
    long EpisodeMasterId,
    string RelativePath,
    string FileName,
    string Sha256,
    string GitObjectId,
    string HashAlgorithm,
    string SourcePath,
    bool MetadataOnly,
    long FileSize,
    long LastWriteTimeUtcTicks);

public sealed record SceneAssetCacheDocument(
    int Version,
    string MasterDataVersion,
    string SourceRevision,
    DateTimeOffset GeneratedAt,
    IReadOnlyDictionary<string, SceneAssetCacheEntry> Assets);

public sealed record SceneAssetCacheBuildResult(
    string OutputPath,
    int AssetCount,
    int MatchedJsonCount,
    int MissingJsonCount,
    long TotalBytes);

public sealed class SceneAssetCacheService
{
    public SceneAssetCacheBuildResult Build(
        string episodeDirectory,
        string sceneDirectory,
        string outputPath,
        SceneAssetCacheOptions options,
        bool overwrite);

    public SceneAssetCacheDocument Load(string cachePath);
}
```

`Build` 负责路径验证、递归扫描、ID 匹配、哈希/元数据收集、重复检测、JSON 输出和覆盖保护。`Load` 负责读取并验证 Version 2 缓存，使 GUI 可以在生成后重新打开和显示摘要。文件写入使用现有 `AtomicFile`。

新增 `Sirius.AssetTool.Episodes.EpisodeEditorService` 作为编辑器的数据边界：

- `Load` 使用 `EpisodeCodec` 验证输入，同时保留包装 JSON 的未知顶层字段；
- 编辑文档暴露可变的 `EpisodeDetailResult` 列表；
- `Save` 用更新后的 `EpisodeDetail` 替换原 JSON 节点，保留其它包装元数据；
- `Pack` 复用 `EpisodeCodec.Pack` 和 `EpisodeCodec.Verify`；
- JSON 使用 UTF-8、缩进格式和非 ASCII 不转义，避免中文再次显示成 `\\uXXXX`。

## ToolboxUI 窗口

### 剧情资源缓存窗口

独立窗口提供：

- Episode JSON 目录选择；
- scene BIN 目录选择；
- 输出 `scene-assets.json` 路径选择；
- MasterData 版本、源代码版本输入；
- “仅元数据”选项；
- 建立缓存按钮和中文日志；
- 生成后显示资产数量、匹配 JSON 数量、缺少 JSON 数量和总字节数；
- 打开已有缓存并显示基本信息及资产表格。

### 简易剧情编辑器

独立窗口提供：

- 打开 JSON、保存、另存为；
- 左侧剧情段列表，至少显示顺序、说话人和正文摘要；
- 右侧编辑常用字段：`Order`、`GroupOrder`、`SpeakerName`、`Phrase`、`Title`、`Effect`、`BackgroundImageFileName`、`BgmFileName`、`SeFileName`、`VoiceFileName`；
- 新建、复制、删除剧情段；
- 应用当前段修改，切换段落前提示未应用修改；
- 保存 JSON 后可选择输出 BIN，并显示记录数和校验结果；
- 未编辑的角色动作、淡入淡出、镜头和其它协议字段保持不变。

两个窗口都遵循现有 ToolboxUI 生命周期：从开始界面打开时隐藏开始界面，关闭子窗口后恢复并激活开始界面；重复打开只激活已有窗口。所有耗时文件操作异步执行，执行期间禁用相关按钮。

## CLI 兼容

现有 Episode 命令行为保持不变。为方便自动化，增加可选的缓存命令：

```text
Sirius.AssetTool episode cache <episode目录> <scene目录> [-o scene-assets.json]
    [--metadata-only]
    [--master-data-version <value>]
    [--source-revision <value>]
    [--force]
```

CLI 只负责参数解析和结果投影，实际工作调用 `SceneAssetCacheService`。

## 错误处理

- 输入目录不存在、没有可识别文件、重复 Episode ID、JSON 无法验证时返回中文错误；
- 已存在的缓存、JSON 或 BIN 默认拒绝覆盖，必须显式勾选覆盖或传 `--force`；
- 单个缓存条目读取失败时构建失败，不生成半成品缓存；
- 编辑器保存失败时保留当前内存内容，不关闭窗口；
- 密钥、路径之外的敏感信息不写入日志。

## 测试标准

服务级测试使用临时中文路径和嵌套目录，覆盖：

- 缓存 Version 2 文档生成、JSON/BIN ID 匹配、相对路径、SHA-256、元数据模式和重复 ID 拒绝；
- 缓存加载和资产统计；
- 编辑器包装 JSON 加载/保存时保留元数据和中文，新增/删除/修改剧情段；
- 编辑后 BIN 打包和解包往返；
- 既有 Chart/Episode 批处理回归。

验证命令包括解决方案构建、服务 harness、Chart 自测、Episode 帮助、smoke test 和无数据库 ToolboxUI 启动烟测。没有显示环境时不把原生控件自动化作为唯一验收条件。

## 非目标

- 不实现角色立绘、背景、语音的预览播放器；
- 不编辑 MasterData 表；
- 不伪造 GitObjectId；
- 不改变 Episode BIN 的 MessagePack/LZ4 协议；
- 不删除或改写用户已有的 `scene-assets.json`，除非用户显式允许覆盖。
