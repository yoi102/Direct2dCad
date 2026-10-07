# Direct2dCad 代码审查

本文件保留修复前的审查结论与复现证据。下述 5 项问题现已修复，最终实现和验收见 [修复说明](CODE-REVIEW-FIXES-2026-10-07.md)。源码链接指向现有文件；下文的缺陷描述和验证数字属于修复前的历史记录。

2026 年 10 月 7 日，基于 `8766f612efb780c2df777ae38b3ef3b4b3a93a59` 及当时工作树。审查时确认 5 项尚未修复的问题：两项影响图纸内容和撤销一致性，三项涉及视口尺寸、AI 取消后的上下文以及失败操作残留。此前快捷键与 Esc 修改保留。审查阶段只新增审查文档和隔离复现材料，没有修改产品实现。

## 已确认的问题

### P1 缩放失败仍修改实体且没有撤销记录

选中一条直线和一个原始比例为 `1e-8` 的合法块参照，绕原点执行 `0.01` 倍缩放。通过共享工具 `transform_entities` 实测返回 `success:false`，但直线起点和块插入点均从 `(100,100)` 变成 `(1,1)`；`CanUndo=false`、文档版本仍为 `0`。

[ScaleEntitiesCommand](../Direct2dCad.Commands/TransformEntitiesCommands.cs)逐个就地修改实体；[预检查](../Direct2dCad.Commands/CadEntityTransform.cs)只验证缩放因子及标注比例，没有验证块的最终比例。块参照执行时先移动位置，再因最终比例不满足数据库下限而抛异常。失败命令没有进入历史，外层工具批次无法回滚这一条命令的内部修改，空间索引和保存脏状态也无法依赖正常变更通知获知该修改。

应先构造并验证所有实体的目标几何，再统一提交；失败时保持几何、索引、文档版本和历史不变。回归应覆盖块比例下限、数值上溢及混合实体，而非仅检查不支持的类型。

### P1 撤销删除布局会恢复此前已经删除的图形

在纸空间删除一条直线，再删除该布局，最后只撤销“删除布局”。实测直线从 `IsErased=true` 变成 `false`；历史标识已回到删除布局前的同一个状态，但实体内容并不相同。如果此前状态被标记为已保存，保存会话依赖的历史比较也无法识别这种内容偏差。

[DeleteLayoutCommand.Undo](../Direct2dCad.Commands/LayoutCommands.cs)对纸空间块中的每一个实体无条件 `Restore()`，没有记录执行前的删除状态。纸空间块的实体集合保留软删除对象，所以会一并恢复旧对象。

应记录删除布局时各实体原有的 `IsErased` 状态，并准确恢复。验收要包含布局中同时存在存活和已删除实体、撤销与重做，以及回到保存基线后的内容一致性。

### P2 工具创建或更新布局视口会错误改变尺寸

调用 `add_layout_viewport`，传入 `x=20,y=30,width=200,height=120`，实测得到 `180×90` 的视口。随后只调用 `set_layout_viewport` 设置 `locked=true`，视口再次缩小为 `160×60`，两次调用均返回成功。

[创建入口](../Direct2dCad.Application/Tools/CadWorkspaceToolExecutor.Presentation.cs)和[更新入口](../Direct2dCad.Application/Tools/CadWorkspaceToolExecutor.Presentation.cs)把宽、高作为 `CadRectD` 的第三、第四个参数；但[构造函数](../Direct2dCad.Db/Geometry/RectD.cs)接受的是 `maxX,maxY`。省略宽、高的更新也会把当前宽、高重新误当坐标。某些偏移下，原本合法的视口还会被判定尺寸无效。

应统一使用 `CadRectD.FromXYWH` 或显式计算右上角。验收包含非零及负的左下角、只更新锁定或可见性、只更新比例、只更新位置，并断言未指定的尺寸保持不变。界面属性面板的[对应计算](../Direct2dCad.ViewModels/Layouts/LayoutWorkspaceViewModel.cs)已正确相加，本问题定位在共享工具入口，影响 Terminal JSON 和 AI。

### P2 AI 批次取消后丢失已成功操作的上下文

离线模型回放一次返回两个 `add_line` 调用，使用真实工作区执行器完成第一个，在收到它的成功事件时取消运行，然后提交“Continue”。图纸实测保留一条直线，会话内仍有一条工具结果，但下一轮发给模型的请求中工具结果数量为 `0`，只剩系统提示和两条用户消息。

[AgentRunner](../Direct2dCad.Agent/AgentRunner.cs)按顺序执行工具，取消后未补齐剩余调用的终止结果；[NormalizeToolMessages](../Direct2dCad.Agent/AgentRequestContextBuilder.cs)发现一组调用缺少任一结果，就删除整个调用组及已经成功的结果。协议序列合法了，但执行事实丢失，模型继续任务时可能重复绘图或误判进度。

应保留已经提交的成功回执，为未执行或取消的调用补充明确终止状态，使后续请求同时满足协议完整性和执行事实完整性。回归应直接检查最终发送的消息及图纸实体数。此处确认的是本地上下文丢失；没有把真实模型是否会重复调用当作已发生事实。

### P2 创建无效布局会遗留无历史记录的纸空间块

通过 `create_layout` 创建 `10×10` mm 布局，默认每边 `10` mm 的边距使创建失败。实测工具返回 `success:false`，但数据库块数从 `2` 增至 `3`，没有撤销记录，文档版本仍为 `0`。

[CadDocument.CreateLayout](../Direct2dCad.Db/Cad/CadDocument.cs)先创建纸空间块，再由 `CadLayout` 构造函数验证纸张和边距。后者失败时，前面加入的块未清理。

应在创建任何数据库对象前验证完整纸张参数，或对失败构造做精确回滚。验收比较失败前后的布局、块、实体、版本和历史；连续失败也不能累积孤立系统块。

## 审查范围

梳理解决方案项目清单，并对非测试 C# 做全仓风险检索；深入检查的数据流包括命令与撤销、图纸与布局、保存和恢复、快照查询、CLI 与 Agent 工具执行、异步取消、渲染后台准备和资源释放、命中索引、WPF 文档关闭及打印边界。这是关键路径代码审查和定向复现，不代表逐行验证全部源码，也不构成运行性能或外部 CAD 兼容性的完整验收。

此前架构、命令行和椭圆布尔审查的旧问题已与当前实现对照，没有直接照搬历史“待修复”结论。DXF 仍会报告并省略部分不支持的普通实体，独立椭圆、椭圆弧、矩形的直接输出尚未补齐；这与面域边界可输出精确椭圆弧是不同能力。它属于已声明的交换范围限制，不计入上面 5 项新复现缺陷。

## 验证证据

| 验证 | 本轮结果 | 范围 |
| --- | --- | --- |
| 隔离复现程序 | 5 项缺陷均复现 | 调用当前生产项目，检查实际实体、版本、历史和下一轮模型请求 |
| Commands 定向测试 | 28 通过 | 现有变换和布局相关测试 |
| Agent 测试 | 19 通过 | 现有 Runner 和上下文测试 |
| Application 测试 | 55 通过 | 现有无界面工具与 schema 测试 |

102 项现有测试通过与 5 项缺陷复现同时成立，说明这些失败条件尚未被覆盖。测试使用当前 Release 输出及 `--no-build --no-restore`；复现程序通过项目引用重新编译所需生产项目。没有重跑完整解决方案、全部 Windows/UI 自动化、真实模型任务、物理打印或外部 CAD 往返验收。

[复现输出](code-review/2026-10-07/probe-output.txt) · [源码与验证记录](code-review/2026-10-07/verification.json) · [复现程序](code-review/2026-10-07/Program.cs.txt) · [项目配置](code-review/2026-10-07/Probe.csproj.txt)

在仓库根目录复验：

```powershell
New-Item -ItemType Directory -Force TestResults/code-review-2026-10-07 | Out-Null
Copy-Item docs/code-review/2026-10-07/Program.cs.txt TestResults/code-review-2026-10-07/Program.cs
Copy-Item docs/code-review/2026-10-07/Probe.csproj.txt TestResults/code-review-2026-10-07/Probe.csproj
dotnet run --project TestResults/code-review-2026-10-07/Probe.csproj -c Release
```

复现断言期待观察到当前缺陷；修复后应将这些场景改写为正式的正确行为回归测试，不能把“复现成功”记作产品通过。

处理顺序建议为：失败操作原子性与布局撤销、视口尺寸、AI 取消回执、布局创建回滚。产品实现、修复验证、提交和发布均未在本轮执行。
