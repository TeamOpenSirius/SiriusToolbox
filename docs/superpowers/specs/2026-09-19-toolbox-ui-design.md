# ToolboxUI 统一图形界面设计

## 目标

将现有 MasterUI、Chart 工具和 Episode 工具整合为一个名为 `ToolboxUI` 的 Windows Forms 应用。应用启动后显示工具入口中心，用户分别打开三个独立工具窗口；现有 CLI 工具继续可用并复用相同的底层处理逻辑。

## 已确认的用户体验

- 启动程序显示 `ToolboxUI` 开始界面。
- 开始界面提供三个入口：主数据工具、Chart 工具、Episode 工具。
- 每个入口打开一个独立窗口，而不是在主窗口中使用 Tab 页。
- 打开子窗口时隐藏开始界面；子窗口关闭后恢复并激活开始界面。
- 所有新增 GUI 文案使用中文。
- GUI 操作失败时在对应窗口中显示中文错误信息，不让异常直接导致进程退出。

## 窗口职责

### ToolboxUI 开始界面

负责应用启动、显示三个工具按钮、打开或激活对应窗口，以及在关闭时关闭子窗口。它不包含业务处理逻辑。

### 主数据工具窗口

沿用当前 `MainForm` 的数据库打开、表浏览、分页、JSON 编辑、增删改、校验和保存功能。该窗口改名为 `MasterToolForm`，避免与应用入口窗口混淆。

### Chart 工具窗口

提供现有 Chart CLI 的四项操作：

- SUS 转换为 Sirius 文本；
- SUS 转换并编码为 ENC；
- ENC 解码为文本；
- Chart 加解密自测。

输入文件、输出文件、密钥和现有选项（忽略 WaveOffset、严格模式、编码时输出中间文本）均在窗口中配置。长时间操作通过异步任务执行，界面显示状态和结果日志。

### Episode 工具窗口

提供现有 Episode CLI 的五项操作：

- JSON 打包为 BIN；
- 目录批量打包；
- BIN 解包为 JSON；
- BIN 目录批量解包为 JSON；
- BIN 检查。

输入路径、输出路径和覆盖选项在窗口中配置。批量操作的逐文件结果汇总到日志区域。

## 代码架构

新增 `Sirius.ToolboxUI` WinForms 项目，作为唯一统一 GUI 启动项目。现有 `Sirius.MasterUI` 项目和程序集迁移为 `Sirius.ToolboxUI`，保留原来的 MasterMemory 依赖。

Chart 和 Episode 的业务操作从 `Sirius.AssetTool` 的 CLI 命令内部提取到可复用的公共服务/结果类型。CLI 命令和 GUI 都调用这些服务；GUI 不启动外部进程，也不复制转换或编解码算法。

推荐的公共边界如下：

- `ChartToolService`: `ConvertToText`, `Encode`, `Decode`, `SelfTest`；返回输出路径、音符数量和警告等结果。
- `EpisodeToolService`: `Pack`, `PackDirectory`, `Unpack`, `UnpackDirectory`, `Inspect`；返回输出路径、统计数据和逐文件诊断结果。

服务负责文件存在性、覆盖策略和输入校验；WinForms 窗口负责路径选择、参数绑定、异步调度和中文呈现。

## 命名与兼容性

- GUI 项目、程序集和根命名空间统一使用 `Sirius.ToolboxUI`。
- 主入口类为 `ToolboxHomeForm`，三个工具窗口分别为 `MasterToolForm`、`ChartToolForm`、`EpisodeToolForm`。
- `Sirius.AssetTool` 和 `Sirius.MasterTool` CLI 项目继续保留，现有命令行用法不改变。
- 旧 `Sirius.MasterUI` 项目不再作为单独 GUI 项目加入解决方案；主数据窗口代码迁移到新的 ToolboxUI 项目。

## 错误处理与生命周期

- 窗口操作使用 `try/catch`，错误显示在日志或错误对话框中。
- 操作期间禁用相关执行按钮，避免重复写文件。
- 主窗口关闭时关闭所有子窗口并释放数据库工作副本。
- Chart 密钥不写入日志。
- 文件写入继续遵守现有覆盖确认/强制覆盖语义。

## 验证标准

- 解决方案可完整构建，0 警告、0 错误。
- ToolboxUI 可启动并显示开始界面。
- 三个入口都能打开对应窗口，重复点击会激活已有窗口而不是创建无限实例。
- CLI Chart/Episode 命令的现有行为不回归。
- GUI 的关键服务边界有自动化测试；无显示环境时至少通过服务级测试和构建验证。
