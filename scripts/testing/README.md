# 测试与覆盖率

最新扩展范围、修复项与验证边界见 [覆盖扩展记录](COVERAGE.md)。

## 运行

在 Windows 和解决方案所需的 .NET SDK 环境中运行。脚本先验证覆盖率汇总器，再构建整个解决方案，随后逐个运行测试项目，避免并发构建和原生资源竞争。

```powershell
# 托管回归及覆盖率
.\scripts\testing\Run-Regression.ps1 -CollectCoverage

# 加入真实 Direct2D/DirectWrite 集成测试和独立应用 UI 测试
.\scripts\testing\Run-Regression.ps1 -CollectCoverage -IncludeWindowsIntegration -IncludeUiAutomation
```

默认使用 Release。`-Configuration Debug` 切换配置，`-NoBuild` 只适用于已构建且源码未改变的同一配置。
结果默认写入带时间戳的 `TestResults/regression-*`，可通过 `-ResultsDirectory` 指定。任何测试项目失败都会令脚本失败。

UI 自动化需要可交互、未锁定的 Windows 桌面，并会启动测试应用。默认排除会改写系统剪贴板的图片/OLE 粘贴用例。
只有在专用测试桌面上，才添加 `-IncludeClipboardTests`；该用例不会恢复任意第三方 OLE 剪贴板内容。

## 覆盖范围

| 测试项目 | 主要责任 |
| --- | --- |
| `Direct2dCad.Db.Tests` | 实体几何、样式、层、块、布局及几何缓存 |
| `Direct2dCad.Commands.Tests` | 属性修改、变换、批量操作、层/块/布局、剪贴板及 undo/redo |
| `Direct2dCad.Editor.Tests` | 命令历史、选择状态、空间归属、索引及资源变更分发 |
| `Direct2dCad.HitTesting.Tests` / `Direct2dCad.Indexing.Tests` | 命中规则、空间查询、增量更新 |
| `Direct2dCad.IO.Tests` | 文件往返、二进制快照、取消、协作采集及原子保存 |
| `Direct2dCad.Tests` | 跨层契约、脏区域、布局投影、缓存与选择 |
| `Direct2dCad.ViewModels.Services.Tests` | 保存会话、异步排队、取消及变更状态 |
| `Direct2dCad.ViewModels.Tests` | 实体属性面板、绘制、grip、粘贴、命令行、设置、标签页保存/关闭及消息订阅 |
| `Direct2dCad.Agent.Tests` / `Direct2dCad.Agent.Codex.Tests` / `Direct2dCad.AI.Tests` | Agent 协议、模型、工具 schema 和上下文契约，不访问真实在线账户 |
| `Direct2dCad.Windows.IntegrationTests` | 原生绘制与像素比较、资源生命周期、打印转换和 OLE 桥接 |
| `Direct2dCad.UiAutomation.Tests` | 独立 WPF 进程中的关键交互流程 |

新增的参数化交互矩阵使用 15 类样本：Line、Circle、Ellipse、EllipseArc、Arc、Rectangle、开放 Polyline、闭合 Polygon、Spline、Text、ShapeText、Image、OLE、BlockReference、CompositePath。
Polygon 在模型中是闭合的 `CadPolyline`，不是另一种实体类。

- 各类实体：名称、层、ZIndex、可见性、可支持的外观与填充、删除后的旧面板防护。
- 各个可用 grip：预览不修改原实体、提交、取消、锁定、undo/redo；混合路径另外校验圆弧/样条拓扑。
- 24 种实际绘制模式：瞬态设置、完成、取消、实体类型、属性和历史；插块、布局视口及原点不计入这 24 种。
- 各类实体跨文档粘贴：独立快照、预览、目标层、移动定位、重复粘贴、块定义复用和 undo/redo。
- 命令行：绘制别名、点输入、相对坐标、单位换算、无文档与错误输入。
- 设置与文档生命周期：应用/重置隔离、持久化失败、栅格配置增删改、保存/关闭选择、修改星号、工具栏同步。
- Agent 实际执行：12 种创建入口、15 类样本的几何读写/复制/公共属性、类型能力拒绝、填充与笔画、字体复用、图片导入、OLE 数据修改、样式与块生命周期；不只验证 schema。
- 命令事务：失败保留 redo、仅回滚本次调用、不撤回同批次较早的成功调用、延迟历史裁剪、只读查询不改历史、拒绝事务期间的历史重入。
- 多文档主窗口：欢迎页/文档/工具箱激活、打印状态、已有文档定位、多个未保存文件的保存/放弃/取消及保存失败。
- 图层/块面板：异步确认期间切换、切回、卸载文档，旧列表项不得编辑新文档；图层面板另覆盖释放期间的确认。
- Layout：新增/重命名/删除、实时设置、非法尺寸恢复、视口创建/切换/锁定/隐藏/删除和 Escape。
- 选择矩阵：15 类样本的框选/跨选/过滤、编辑器历史与文档历史隔离，以及旋转图片边框。
- CompositePath 的 Direct2D 预览：真正的混合曲线、填充、移动局部刷新与完整帧对比、缓存复用和释放。

矩阵验证的是各类实体的共同契约，不表示每一个专属属性、参数组合或 UI 布局均已穷举。

## 查看覆盖率

`-CollectCoverage` 使用 Coverlet 生成每个项目的 `coverage.cobertura.xml`，并汇总成：

- `coverage-projects.csv`：各程序集可执行代码行的覆盖率。
- `coverage-files.csv`：文件覆盖率与未覆盖行数。
- 各测试项目目录中的 `.trx`：实际通过、失败和跳过的用例。

也可单独合并同一版本代码的测试报告：

```powershell
.\scripts\testing\Get-CoverageSummary.ps1 -ResultsDirectory .\TestResults\your-run -OutputDirectory .\TestResults\your-run
```

合并先根据 Cobertura 的 `sources` 解析实际源文件路径，再按程序集、规范路径和行号去重，排除测试程序集、`obj`、`.g.cs` 和 `.Designer.cs`。不同测试项目可能使用不同的源目录，不能直接按 XML 中的相对文件名合并。
`Test-CoverageSummary.ps1` 用固定报告验证不同源根、重复类和生成文件排除；它也会随完整回归运行。
这是**可执行行覆盖率，不是分支覆盖率或业务完整度**。不要合并不同代码版本的报告来声称覆盖率提高。
UI 测试启动的 WPF 子进程没有被 Coverlet 插桩，因此 UI 操作不会计入该汇总。

## 仍需验收

- 真实 Word/Excel 等外部 OLE Server 的编辑、`IAdviseSink` 回调及服务端异常。
- 真实打印机、PDF 驱动、DPI 和可选择文字；多 GPU 驱动、设备移除、多屏与混合 DPI。
- 完整 UI 的焦点、布局、拖放和各实体专属属性组合。
- 固定大图纸上的性能基准；测试通过或行覆盖率提高不能证明 FPS 提升。
