# Direct2dCad 绘制缺陷修复与验收

2026 年 10 月 7 日，[绘制审查](DRAWING-REVIEW-2026-10-07.md)确认的四项问题已完成实现：倾斜椭圆构造、大坐标短线消失、多段线虚线预览不一致，以及闭合样条填充图案位移。本文记录本次修改及验收；此前的快捷键、生命周期和其他代码审查修复保留，不计入本次新增成果。

## 当前行为

| 问题 | 修复后的行为 | 原始场景验收 |
| --- | --- | --- |
| 倾斜椭圆轴方向丢失 | 第一轴按真实长度和方向建立局部坐标系，另一轴取垂直投影；旋转参数经过预览、动态输入和创建命令传到实体 | 中心、轴端点及椭圆弧三种方式均得到半轴 56.568542/14.142136、旋转 π/4，指定端点代入局部椭圆方程为 1 |
| 大坐标短线不可见 | 提交单精度几何前减去局部原点，以双精度组合视口、块和临时变换 | 百万坐标长 0.01 的线恢复 80 个红色像素；一亿坐标长 0.5 的线恢复 400 个，与原点对照相同 |
| 多段线虚线在每段重启 | 开放及无填充闭合预览也使用完整路径，统一虚线相位、端帽和连接 | 原始 Dash/Bevel 场景的预览与实体像素差从 423 降至 0 |
| 闭合样条填充图案位移 | 预览和实体共用包含贝塞尔控制点的边界计算 | 原始水平线图案的预览与实体像素差从 27,366 降至 0，实心填充对照仍为 0 |

## 实现与兼容性

[CadDrawingGeometryFactory](../Direct2dCad.ViewModels.Services/Geometry/CadDrawingGeometryFactory.cs)保留轴方向和旋转角。第一轴主要沿水平方向时仍对应 X 半轴，主要沿垂直方向时仍对应 Y 半轴；正反输入归一到相同的局部轴含义。中心方式修改半轴保持中心，轴端点方式保持第一个端点；椭圆弧修改半轴保持已经确认的局部起始参数角。零轴和共线第二轴继续拒绝提交，并保留可修正的绘图步骤。

[Direct2DCoordinateSystem](../Direct2dCad.Rendering.Direct2D/Resources/Direct2DCoordinateSystem.cs)在坐标作用域中保留双精度变换，[Direct2DLocalGeometry](../Direct2dCad.Rendering.Direct2D/Resources/Direct2DLocalGeometry.cs)为远坐标实体生成局部几何。资源桶保存局部原点和几何副本，后台准备与细节几何使用同一基准。数据库中的世界坐标、ID、层和嵌入数据不因绘制改变；图片位图仍从原实体载荷取得。选择、无资源预览、平移/分组预览、旋转、块展开、纸空间、网格与夹点接入相同坐标规则。原生几何及实际 GPU 提交仍为单精度，不能把本次修复解释为任意尺度下的数学精确渲染。

局部副本在几何、外观、图层、填充、透明度和旋转变化后刷新，避免后续绘图使用旧参数。大坐标继续保留原有的 geometry realization 安全回退，使用直接几何绘制。远坐标视图或涉及远坐标块定义时，整景命令列表、块定义列表、场景瓦片及相关预览组按安全条件回退；每实体几何资源仍可复用。普通视图不会仅因为存在远处的离屏模型实体就关闭整景缓存。缓存准备能够结束，不通过持续重建掩盖精度问题。

这条回退路径以几何正确性为目的，可能增加远坐标大场景的 CPU 提交开销。当前没有测量物理显示延迟、真实窗口 FPS 或 GPU 性能，不据此作性能提升承诺。

[CadSpline.CalculateBounds](../Direct2dCad.Db/Data/Entities/CadSpline.cs)统一拟合点与控制点边界；临时样条填充及临时场景边界使用该计算。[Direct2DTransientRenderer](../Direct2dCad.Rendering.Direct2D/Transient/Direct2DTransientRenderer.cs)使用完整多段路径绘制预览。

## 验证

最终回归在当前工作树的 Release 构建执行：

| 验证 | 结果 |
| --- | --- |
| 架构依赖与规则夹具 | 41 项目、123 引用无循环或禁止依赖；11 项规则夹具通过 |
| 完整解决方案 Release 构建 | 0 警告、0 错误 |
| 13 个托管测试项目 | 1,986 通过，0 失败 |
| Windows/Direct2D 集成 | 537 通过，0 失败，1 跳过 |
| 原始四项缺陷的正确行为探针 | 全部通过，实线和实心填充对照通过 |
| 隔离真实窗口冒烟 | 7 通过，0 失败；椭圆交互绑定日志为空 |

跳过项 `SuppliedDxfLongStrokesPreserveFullPathRasterCoverageAcrossZoomAndPan` 需要本机尚未提供的 `DIRECT2DCAD_UI_DXF_SAMPLE`。新增正式用例包括：

- [EllipseAxisDrawingTests](../Direct2dCad.ViewModels.Tests/EllipseAxisDrawingTests.cs)：12 项，覆盖中心/轴端点、斜轴与水平/垂直/反向轴、数值修改、预览/提交一致性、共线拒绝、椭圆弧参数及撤销重做。
- [DrawingConsistencyPixelTests](../Direct2dCad.Windows.IntegrationTests/DrawingConsistencyPixelTests.cs)：143 项，覆盖多段线端帽/连接、带角度和非零原点的样条图案，以及实体、选择、后台准备、平移、无资源/图元预览的大坐标像素对照；还覆盖文字、图片、块、嵌套块、纸空间、网格、夹点、元数据刷新、编辑撤销重做、局部帧和 1,100 实体场景。
- [MainWindowUiTests.TiltedEllipse](../Direct2dCad.UiAutomation.Tests/MainWindowUiTests.TiltedEllipse.cs)：3 项，实际窗口通过 Terminal 构造斜轴，操作半轴输入框，并查询实际实体的 45° 旋转与半轴值，检查撤销重做。

真实窗口测试使用最终 Release 可执行文件、隔离进程及设置目录，通过 UIA 和真实键鼠完成操作；另外复验已有椭圆弧参数、布局切换、Terminal 工具和布尔交互。只关闭测试进程，未重启用户原有窗口，也未运行剪贴板测试。已打开的旧程序需要重新启动才会加载新构建。

像素验收使用实际 Direct2D 宿主的 640×480 离屏帧，关闭抗锯齿以进行逐像素比较。启用 realization 选项的远坐标场景仍按安全条件使用直接几何回退。原始探针保留实线与实心填充对照。正式用例与原始探针分别记录，不重复相加为全量用例数。

外部 DXF 样本、外部 CAD 往返和人工窗口验收不属于这些自动化结果。OLE 原生内容回调及跨设备性能也没有新增专项大坐标验收；本次没有改动 OLE 的数据与回调路径。普通视图的瓦片回放和大坐标 realization 回退沿用原测试的断言，兼容问题修复后通过，未放宽验收条件。

## 证据与复验

[验收程序](code-review/drawing-fixes-2026-10-07/Program.cs.txt) · [项目配置](code-review/drawing-fixes-2026-10-07/Probe.csproj.txt) · [原始场景修复输出](code-review/drawing-fixes-2026-10-07/probe-output.txt) · [最终回归日志](code-review/drawing-fixes-2026-10-07/regression.txt) · [真实窗口日志](code-review/drawing-fixes-2026-10-07/ui-smoke.txt) · [源码、二进制及证据哈希](code-review/drawing-fixes-2026-10-07/verification.json)

[多段线预览](code-review/drawing-fixes-2026-10-07/polyline-preview.png) · [提交实体](code-review/drawing-fixes-2026-10-07/polyline-committed.png) · [样条图案预览](code-review/drawing-fixes-2026-10-07/spline-hatch-preview.png) · [提交样条](code-review/drawing-fixes-2026-10-07/spline-hatch-committed.png) · [百万坐标短线](code-review/drawing-fixes-2026-10-07/far-from-origin-million.png)

真实窗口：[倾斜中心椭圆](code-review/drawing-fixes-2026-10-07/ui-screenshots/tilted-ellipse-center.png) · [倾斜轴端点椭圆](code-review/drawing-fixes-2026-10-07/ui-screenshots/tilted-ellipse-axis.png) · [倾斜椭圆弧](code-review/drawing-fixes-2026-10-07/ui-screenshots/tilted-ellipse-arc.png)。画面中的 FPS 是应用内即时指标，没有计入性能验收。

原始 [绘制缺陷材料](code-review/drawing-2026-10-07/verification.json)保持不变，其中断言期待旧缺陷，不能作为修复通过的证据。当前工作树还含此前授权的未提交修改，HEAD 仅作基准；实际验收绑定归档的源码和 Release 二进制 SHA-256。

在仓库根目录复验：

```powershell
& scripts/testing/Run-Regression.ps1 -Configuration Release -IncludeWindowsIntegration `
    -ResultsDirectory TestResults/drawing-fixes-verification

dotnet test Direct2dCad.ViewModels.Tests -c Release --filter FullyQualifiedName~EllipseAxisDrawingTests
dotnet test Direct2dCad.Windows.IntegrationTests -c Release --filter FullyQualifiedName~DrawingConsistencyPixelTests
```

独立探针的归档文件以 `.txt` 保存；复制到 `TestResults/drawing-fixes-2026-10-07`，分别还原为 `Program.cs` 和 `Probe.csproj`，再运行 `dotnet run --project TestResults/drawing-fixes-2026-10-07/Probe.csproj -c Release`。没有提交、推送或发布。
