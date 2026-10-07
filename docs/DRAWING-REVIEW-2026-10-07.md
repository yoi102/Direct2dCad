# Direct2dCad 绘制模块审查

2026 年 10 月 7 日，当前绘制模块确认 4 项问题：倾斜椭圆构造错误、大坐标下短线无法显示、多段线预览与最终图形不一致，以及样条填充图案在确认后位移。现有相关测试通过，但没有覆盖这些条件。本记录保存修复前的审查结论和独立复现材料。四项问题的后续实现及当前验收见[绘制修复文档](DRAWING-FIXES-2026-10-07.md)。以下源码描述属于审查时的历史状态，当前实现已改变。

## 已确认的问题

### 倾斜椭圆轴的方向和长度被丢弃

中心方式依次输入中心 `(0,0)`、第一轴端点 `(40,40)` 和另一轴点 `(-10,10)`，生成的实体半轴为 `40,10`、旋转角为 `0`。指定的第一轴端点代入结果椭圆方程得到 `17`，应为 `1`，所以它没有落在所画的椭圆上。轴端点方式输入 `(-40,-40)`、`(40,40)`、`(-10,10)` 也出现相同错误；椭圆弧构造复用该计算，同样受影响。

[中心构造](../Direct2dCad.ViewModels.Services/Geometry/CadDrawingGeometryFactory.cs)和[轴端点构造](../Direct2dCad.ViewModels.Services/Geometry/CadDrawingGeometryFactory.cs)根据轴向量哪个坐标分量较大来选择世界 X/Y 轴，没有保留轴向量的长度和方向。[创建入口](../Direct2dCad.ViewModels.Services/Drawing/CadDrawingEntityCreator.cs)和[预览](../Direct2dCad.ViewModels.Services/Drawing/CadEllipseDrawingPreviewBuilder.cs)也未传递旋转角。现有实体和渲染器能显示旋转椭圆，问题在交互构造链。

优先级为 P2。修复应以第一轴的长度和方向建立局部坐标系，把第二轴输入投影到垂直方向，并让预览、动态数值输入、实际实体和椭圆弧共用同一组参数。验收应覆盖水平、垂直、45°、任意角度、反向轴和修改半轴后固定轴端点，不能只在创建后另加一次旋转。

### 大坐标下高倍放大时短线消失

在 `(1,000,000,1,000,000)` 附近画长 `0.01` 的水平线，缩放为 `1000` 并把它置于屏幕中心。双精度视口计算期望显示为 `10` 像素长，实际 Direct2D 帧中该线的红色像素数为 `0`；同一条线移到原点后有 `80` 个红色像素。另一个坐标 `100,000,000`、长度 `0.5`、缩放 `100` 的对照也从 `400` 个红色像素变成 `0`。

[直线提交](../Direct2dCad.Rendering.Direct2D/Entities/Direct2DEntityRenderer.cs)先通过 [ToVector2](../Direct2dCad.Rendering.Direct2D/Entities/Direct2DEntityRenderer.cs)把绝对世界坐标转成 `float`；[视口平移](../Direct2dCad.Rendering.Direct2D/Scene/Direct2DSceneRender.cs)也转成单精度。前者已把两个近邻端点量化到同一点，后续平移与放大无法恢复原精度。实体仍存在于数据库，屏幕没有正确显示它。

优先级为 P2，涉及远离原点的精细绘图和导入图纸。修复应在转成 GPU 单精度之前用双精度减去局部原点，再提交局部坐标；几何缓存、平移、块参照和预览需要采用一致的坐标基准。验收应比较同一几何在不同世界位置下的可见长度和像素覆盖，包含放大、平移、选择、缓存回放及纸空间，而不是仅调整线宽或禁用细节简化。

### 多段线预览在每个顶点重新开始虚线

使用三点折线 `(-100,-60) → (-53,-60) → (-53,60)`、Dash 虚线、Bevel 连接和 `3` mm 线宽。在相同颜色、实际显示线宽、视口和抗锯齿配置下，预览与最终实体有 `423` 个像素不同。直线形状的实线多段线对照为 `0`，排除了线宽换算、颜色和变换不一致。

[DrawPolyline](../Direct2dCad.Rendering.Direct2D/Transient/Direct2DTransientRenderer.cs)在开放多段线或没有填充的闭合轮廓上逐段调用 `DrawLine`。每次调用都重置虚线起始位置，还把端帽应用到每一段，而最终实体用完整路径 `DrawGeometry`，连接样式也按完整轮廓处理。问题影响绘图预览和经过同一函数的高亮等临时轮廓。

优先级为 P2。修复应让带虚线、端帽或连接样式的预览采用完整路径；如保留简单实线快速路径，应证明它与整条路径的结果一致。验收应覆盖开放/闭合、无填充、不同端帽、Miter/Bevel/Round 连接和连续虚线相位。

### 闭合样条的填充图案在确认后位移

四个拟合点 `(-50,-50)`、`(50,-50)`、`(50,50)`、`(-50,50)` 构造闭合样条，设置间隔 `6` 的水平线填充。相同样条预览与最终实体有 `27,366` 个像素不同，实心填充对照为 `0`。曲线轮廓保持相同，确认后图案原点改变。

[预览填充](../Direct2dCad.Rendering.Direct2D/Transient/Direct2DTransientRenderer.cs)只用拟合点包围盒 `[-50,50]`。[实体边界](../Direct2dCad.Db/Data/Entities/CadSpline.cs)还包含贝塞尔控制点，此例为 `[-66.6667,66.6667]`。[填充原点](../Direct2dCad.Rendering.Direct2D/Entities/Direct2DHatchRenderer.cs)依赖传入边界的左上角，因此两条路径的原点不同。像素对比确认的是图案位移，没有把可能的其他裁剪影响当作已经复现的事实。

优先级为 P2。修复应统一样条边界及填充原点的计算；相同曲线参数在预览、创建、选择和临时平移时应采用同一规则。验收应覆盖控制点超出拟合点包围盒、开放/闭合、带角度或非零原点的图案、不同缩放，以及实心填充对照。

## 验证结果

| 验证 | 结果与范围 |
| --- | --- |
| 绘图相关 ViewModels 测试 | 202 通过，0 失败，覆盖绘图流程、动态输入、无效输入和会话恢复 |
| Direct2D 相关 Windows 测试 | 187 通过，0 失败，1 跳过，覆盖渲染宿主、缓存、椭圆面域、预算、长多段线和几何准备 |
| 交互构造复现 | 当前生产 ViewModel 接收绘图点，检查实际创建实体；测试平台替代原生窗口服务 |
| 预览与实体像素对比 | 实际 Direct2D 渲染宿主、640×480 离屏帧，含匹配样式的实线和实心填充对照 |
| 大坐标对比 | 实际 Direct2D 渲染宿主，几何平移前后比较屏幕覆盖，已关闭细节简化 |

跳过项需要外部 `DIRECT2DCAD_UI_DXF_SAMPLE`。当前结果不包含真实窗口的鼠标回放、外部 CAD 图纸往返、物理显示延迟或 GPU 性能基准。像素复现关闭抗锯齿与几何 realization，以隔离路径和坐标差异；它证明这些路径存在错误，不给出所有设置组合的失败比例。

审查涉及绘制步骤与构造、预览样式、图元/填充提交、视口变换、可见性和渲染刷新，并抽查资源生命周期、后台录制与异常清理。当前 `ImageSourceDirect2DResource` 已有回调异常后的 `AbortDraw`，没有把历史记录中的旧风险重新计入本次已确认缺陷。这是关键路径审查，不是逐行正确性证明。

## 复现证据

[程序](code-review/drawing-2026-10-07/Program.cs.txt) · [项目配置](code-review/drawing-2026-10-07/Probe.csproj.txt) · [输出](code-review/drawing-2026-10-07/probe-output.txt) · [源码与证据哈希](code-review/drawing-2026-10-07/verification.json)

多段线：[预览](code-review/drawing-2026-10-07/polyline-preview.png) · [最终实体](code-review/drawing-2026-10-07/polyline-committed.png)

样条图案：[预览](code-review/drawing-2026-10-07/spline-hatch-preview.png) · [最终实体](code-review/drawing-2026-10-07/spline-hatch-committed.png)

大坐标：[原点附近](code-review/drawing-2026-10-07/near-origin-million.png) · [百万坐标附近](code-review/drawing-2026-10-07/far-from-origin-million.png)

在仓库根目录复验：

```powershell
New-Item -ItemType Directory -Force TestResults/drawing-audit-2026-10-07 | Out-Null
Copy-Item docs/code-review/drawing-2026-10-07/Program.cs.txt TestResults/drawing-audit-2026-10-07/Program.cs
Copy-Item docs/code-review/drawing-2026-10-07/Probe.csproj.txt TestResults/drawing-audit-2026-10-07/Probe.csproj
dotnet run --project TestResults/drawing-audit-2026-10-07/Probe.csproj -c Release
```

程序用已有测试项目的隔离上下文创建 ViewModel，并通过反射调用宿主的只读像素捕获方法；没有修改产品可见性或替换渲染算法。断言期待复现上述问题，不能将程序正常退出当作绘制模块通过验收。修复时应将正确行为写成正式回归测试。

处理顺序建议为倾斜椭圆构造、大坐标坐标基准、多段线预览、样条填充原点。实现、修复后的完整回归和发布属于后续工作；本次没有修改产品实现。
