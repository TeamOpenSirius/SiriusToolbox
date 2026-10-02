# 自制 Master 编辑器与官方同步移除设计

## 目标

扩展现有 MasterData 运营编辑器，使用户可以从空白记录创建自制卡面、自制活动和自制音乐相关 Master 数据，并在校验成功后原子写入工作副本。删除官方 MasterData/CDN 下载、认证和同步全链路；保留本地 MasterMemory 编辑、离线重打包以及本地 R2/CDN 发布能力。

## 约束

- `Sirius.MasterData.MasterMemoryDatabaseService` 是唯一的 MasterMemory 读写和校验入口，不在 Toolbox 重写二进制编解码器。
- 新记录必须在临时数据库中完成整批写入，并通过 `Verify` 后才替换编辑器工作副本。
- 卡图、音乐音频、Banner 等二进制资源不由 Master 编辑器生成；编辑器只维护 Master 中的资源 Key/路径，并在提交前检查路径存在性（允许用户明确跳过检查）。
- 现有运营编辑器、通用表/JSON 编辑器和 R2/CDN 本地发布流程继续可用。

## 用户流程

### 自制内容工作台

`MasterOperationsForm` 新增“自制内容”页，包含卡面、活动、音乐三个向导入口。向导共享以下步骤：

1. 选择创建类型和数据库工作副本。
2. 预览将创建的主表和关联表。
3. 填写动态字段；字段来自生成模型的 Master schema，复杂对象/列表使用 JSON 编辑器。
4. 为主键提供自动分配和手动指定两种方式。自动分配从现有最大值向上寻找空位，手动值必须通过重复键检查。
5. 校验外键、必填字段、日期关系和资源路径。
6. 在临时数据库应用全部记录并执行完整 `MasterMemoryDatabaseService.Verify`。
7. 校验成功后替换运营编辑器工作副本，刷新现有业务页并标记未保存；失败时丢弃临时文件，不改变工作副本。

### 音乐向导

第一阶段支持创建 `MusicMaster` 主记录以及模型中明确要求的音乐相关关联记录。向导编辑标题、解锁条件、Long/Cover 等业务字段和资源 Key；所有关联 ID 自动引用本次创建的音乐 ID。具体关联集合由实际生成模型和校验错误驱动，不写入模型不存在的字段。

### 活动向导

第一阶段支持创建 `EventMaster` 主记录和用户选择的活动类型子表记录。向导自动生成活动根 ID，并将活动子表的 `EventMasterId` 外键绑定到该 ID；日期、名称、交换商店/卡池关联在提交前检查。

### 卡面向导

第一阶段支持创建卡面所需的 `CharacterMaster`、`CharacterBaseMaster` 及生成模型中被标记为必需的关联记录。图片/立绘资源以资源 Key 或相对路径填写，提交时执行可配置的文件存在性检查。

## 技术结构

- `MasterCreationModels.cs`：创建草稿、字段值、主键分配、校验结果和创建预览模型。
- `MasterCreationService.cs`：读取 schema、生成默认值、分配主键、构建记录 JSON、在临时数据库应用批次并校验。
- `MasterCreationDefinitions.cs`：卡面、活动、音乐三类向导的表集合、外键规则和业务必填规则。
- `MasterCreationPage.cs`：共享向导 UI、动态字段编辑和预览确认。
- `MasterOperationsForm.cs`：管理临时创建目录、提交成功后的工作副本替换、刷新和撤销状态。

现有 `MasterOperationPlan` 继续用于已存在记录的修改；新建批次使用独立的创建批次模型，避免把“新增多表记录”伪装成单表 patch。

## 官方同步移除

删除以下范围：

- 首页“官方 MasterData / CDN 同步”按钮、窗口和窗口注册入口。
- `MasterDataSyncForm` 及 `Sirius.Toolbox.Master.Sync` 官方认证、下载、校验、导出服务。
- 仅服务于官方同步的 API 客户端、认证状态、下载选项、日志模型和项目引用。
- 官方同步相关启动自检、README、MasterData/同步/CDN 镜像文档和测试。

保留以下范围：

- `MasterMemoryDatabaseService` 的本地验证、增删改、导出和离线重打包。
- `R2SyncForm`、`R2AssetSyncService` 及本地目录到 R2/CDN 的发布能力。
- 资源目录解析、缓存和 R2 增量同步所需的本地模型；若某个类型只被官方下载使用，则一并删除。

## 错误处理与安全边界

- 所有字段转换、外键检查、主键冲突和资源检查错误在预览确认前显示，不写入数据库。
- 批次提交使用临时文件；任何异常删除临时文件并保留原工作副本。
- 保存最终数据库时继续生成 `.bak`。
- 删除官方同步后，启动自检必须验证官方同步入口不存在、R2 发布入口仍存在、自制工作台可构造。

## 验证

- 服务层测试：动态字段转换、默认值、主键分配、外键绑定、重复键、批次回滚和 `Verify` 失败回滚。
- UI 启动 smoke test：主页、MasterOperationsForm、自制向导和 R2SyncForm 可以构造；官方同步类型和入口不存在。
- `dotnet build SiriusTools.sln --no-restore`。
- `dotnet test --no-restore`。
- 使用临时 MasterMemory 数据库手工创建一条音乐、一条活动和一条卡面记录，确认重开数据库后数据存在。
