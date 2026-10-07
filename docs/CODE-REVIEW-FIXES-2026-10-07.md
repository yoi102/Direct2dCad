# Direct2dCad 代码审查修复与验收

2026 年 10 月 7 日，[代码审查](CODE-REVIEW-2026-10-07.md)确认的 5 项缺陷全部修复。完整 Windows 回归还发现一个画布初始化异常，已一并修复。此前快捷键、Esc、Enter 和生命周期修改保留；本文件记录本次缺陷修复，不把那些已有工作计入新增成果。

## 修复结果

| 问题 | 当前行为 | 回归依据 |
| --- | --- | --- |
| 缩放失败仍修改实体 | 在独立几何副本上计算并验证全部目标后统一应用；无效结果不改变实体、索引、文档版本、撤销或重做分支 | 块比例下限、块/圆/点溢出、面域/标注退化、16 种混合实体和精确撤销 |
| 撤销删除布局恢复旧删除对象 | 记录每个实体原有的删除状态，只为原本存活的对象发布删除/恢复变更 | 混合存活/已删除实体、多次撤销重做、索引和保存基线一致 |
| 工具视口位置和尺寸混淆 | 创建与更新统一使用 `CadRectD.FromXYWH`；未指定的尺寸保持不变 | 非零/负坐标、坐标大于尺寸、锁定/可见性/比例更新、移动和撤销重做 |
| Agent 取消后丢失已完成回执 | 保留已返回的结构化执行回执，补齐未执行调用的明确结果；后续请求保留完整调用组 | 成功后取消、返回与取消竞态、执行中取消、工具异常、执行前取消和图像附件顺序 |
| 无效布局创建遗留系统块 | 在分配 ID 和创建纸空间块前完成纸张与边距验证 | 连续失败不增加块/布局，不消耗 ID，不改变版本或历史 |
| 画布尺寸切换时脏区越界 | 在 WPF 图像锁内按实际绑定的 `PixelWidth/PixelHeight` 与目标尺寸裁剪脏区 | 真实 1×1 表面到 524×200 的尺寸切换，完整/局部/列表刷新和 Present 回调 |

### 缩放与撤销

[ScaleEntitiesCommand](../Direct2dCad.Commands/TransformEntitiesCommands.cs)使用 [CadScaleGeometryPlan](../Direct2dCad.Commands/CadScaleGeometryPlan.cs)保留变换前后的几何。预计算复用实体原有的 setter 校验，并补充有限值与最终边界验证；应用前检查全部目标类型，应用异常时恢复已处理对象和当前对象。

撤销使用原始几何，不依赖反向浮点运算。文本撤销恢复此前已测量的边界；重新缩放后仍触发正常的文本测量。图像与 OLE 只保存变换几何，嵌入数据留在原实体中。标注恢复定义及线宽来源，块参照保存解析后的边界。独立副本作为命令字段保留，现有历史载荷估计器可以遍历并计入这些几何数据。

### Agent 取消后的上下文

[AgentRunner](../Direct2dCad.Agent/AgentRunner.cs)在一次工具交换结束时补齐结果，再追加已完成截图的图像消息，保证工具回执连续。未开始的调用返回 `not_executed`、`may_have_committed=false`；执行途中抛出异常的调用返回 `execution_interrupted`、`outcome=unknown`、`may_have_committed=true`，要求模型先检查图纸再决定是否重试。

如果取消与工具返回发生竞态，带布尔 `success` 字段的 JSON 回执作为执行事实保留；任意迟到的普通文本仍按原有约定抑制。取消不会撤销此前已经成功的独立工具操作，也不会启动剩余调用。

### WPF 表面切换

原始完整回归在显示测试窗口时暴露 `D3DImage.AddDirtyRect` 异常：目标尺寸已更新到 524×200，WPF 实际仍绑定 1×1 表面。[D3D11ImageSource](../Direct2dCad.wpf/Controls/D3D11ImageSource.cs)现在按实际像素范围裁剪脏区；替换表面绑定后仍可刷新新尺寸边缘，Present 回调正常执行。没有通过忽略异常或跳过原失败测试处理该问题。

## 自动化验收

最终回归在当前工作树、Release 配置执行，基准提交为 `8766f612efb780c2df777ae38b3ef3b4b3a93a59`。工作树含此前授权但尚未提交的修改，验收绑定的是归档中的受影响源码 SHA-256 和实际 Release 二进制，不将 HEAD 当作全部受测源码。

| 检查 | 结果 |
| --- | --- |
| 架构依赖检查 | 41 项目、123 引用，无循环或禁止依赖；11 项架构规则夹具通过 |
| 完整解决方案 Release 构建 | 0 警告、0 错误 |
| 13 个托管测试项目 | 1,974 通过，0 失败 |
| Windows/Direct2D 集成 | 394 通过，0 失败，1 跳过 |
| 原始 5 个场景的正确行为验收程序 | 5 通过，调用实际生产项目 |
| 画布尺寸与原失败快捷键场景定向测试 | 7 通过 |
| 真实窗口冒烟 | 3 通过，0 失败：布局/纸模型空间、Terminal 工具绘图/测量/预设、布尔预览/提交/取消/历史 |

跳过项 `SuppliedDxfLongStrokesPreserveFullPathRasterCoverageAcrossZoomAndPan` 需要 `DIRECT2DCAD_UI_DXF_SAMPLE`，本机未提供该外部样本。其余 Windows 测试通过不替代该样本验收。

真实窗口测试使用本次 Release `Direct2dCad.exe`，启动隔离进程和设置目录，通过 UIA 与实际鼠标/键盘完成操作；测试只关闭自己的进程，不重启用户原有窗口。没有运行会改动系统剪贴板的测试。

新增 26 个正式回归用例分布在：

- [ScaleAndLayoutFailureTests](../Direct2dCad.Editor.Tests/ScaleAndLayoutFailureTests.cs)：13 个。
- [HeadlessToolExecutionTests.AuditRegression](../Direct2dCad.Application.Tests/HeadlessToolExecutionTests.AuditRegression.cs)：5 个。
- [InterruptedToolExchangeTests](../Direct2dCad.Agent.Tests/InterruptedToolExchangeTests.cs)：6 个。
- [CadDocumentSaveSessionTests](../Direct2dCad.ViewModels.Services.Tests/CadDocumentSaveSessionTests.cs)：1 个保存基线回归。
- [D3DImageResizeTests](../Direct2dCad.Windows.IntegrationTests/D3DImageResizeTests.cs)：1 个真实表面切换回归。

## 证据与复验

[修复验收程序](code-review/fixes-2026-10-07/Program.cs.txt) · [项目配置](code-review/fixes-2026-10-07/Probe.csproj.txt) · [五场景输出](code-review/fixes-2026-10-07/probe-output.txt) · [最终回归日志](code-review/fixes-2026-10-07/final-regression.txt) · [真实窗口日志](code-review/fixes-2026-10-07/ui-smoke.txt) · [验收清单与文件哈希](code-review/fixes-2026-10-07/verification.json)

原始 [缺陷复现材料](code-review/2026-10-07/verification.json)保持不变，其断言期待观察到旧缺陷，不能用于判定修复通过。修复验收程序的断言检查正确行为。Agent 场景使用离线模型回放和真实工作区执行器，直接检查下一轮 `AiChatRequest` 与图纸实体数，没有声称测过真实服务商的 HTTP 请求或模型输出。

在仓库根目录复验最终回归：

```powershell
pwsh -NoProfile -File scripts/testing/Run-Regression.ps1 `
  -Configuration Release -IncludeWindowsIntegration `
  -ResultsDirectory TestResults/code-review-fixes-repeat
```

复验五场景程序：

```powershell
New-Item -ItemType Directory -Force TestResults/code-review-fixes-repeat | Out-Null
Copy-Item docs/code-review/fixes-2026-10-07/Program.cs.txt TestResults/code-review-fixes-repeat/Program.cs
Copy-Item docs/code-review/fixes-2026-10-07/Probe.csproj.txt TestResults/code-review-fixes-repeat/Probe.csproj
dotnet run --project TestResults/code-review-fixes-repeat/Probe.csproj -c Release
```

项目引用通过项目配置文件定位仓库根目录。复验程序应放在上述两层 `TestResults/<目录>` 中；如增加层级，需同步调整项目引用路径。

真实窗口冒烟复验：

```powershell
$env:DIRECT2DCAD_UI_EXECUTABLE = Join-Path (Get-Location) 'Direct2dCad.wpf/bin/Release/net10.0-windows/win-x64/Direct2dCad.exe'
dotnet test Direct2dCad.UiAutomation.Tests/Direct2dCad.UiAutomation.Tests.csproj `
  -c Release --no-build --no-restore `
  --filter 'FullyQualifiedName~LayoutTabsAndPaperModelSpaceSwitchTogether|FullyQualifiedName~AiToolCommands_CreateMeasureAndManageGridPreset|FullyQualifiedName~BooleanRibbonAndContextMenuPreviewCommitCancelAndHistory' `
  --logger trx --results-directory TestResults/code-review-ui-repeat
```

## 验收边界

本次完成 5 项原始缺陷及额外画布异常的实现、回归和文档。没有重启或操作用户已有图纸窗口，没有执行真实 LM Studio/Codex 模型任务、完整 UI 自动化、外部 CAD 往返或物理打印。既有普通实体 DXF 输出范围限制仍见原审查的范围说明。本次没有提交、推送或发布。
