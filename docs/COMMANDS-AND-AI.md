# 命令行与 AI 工具

[返回首页](../README.md) · [绘图与编辑](DRAWING-AND-EDITING.md) · [标注与交换](ANNOTATION-AND-EXCHANGE.md) · [本次验证](commandline-ai-implementation/validation-2026-10-06/README.md)

2026-10-06 整改后目录：43 个基础命令（另有 28 个别名）、14 个常用操作简写（另有 7 个别名）、101 个 Terminal / AI 共用 CAD 工具。LM Studio 另有运行时 discover_tools 帮助工具。别名和同一命令的几何变体不重复计数；这些目录有功能重叠，数量不代表完整 CAD 能力。此前的[运行时命令目录](validation/2026-10-03/command-coverage/command-catalog.json)保留为历史快照。

当前行为及验证见[命令行与 AI Agent 整改](COMMANDLINE-AND-AI-IMPLEMENTATION.md)。原[审查](COMMANDLINE-AND-AI-REVIEW.md)保留整改前复现，不能当作当前缺陷状态。

## 页面和入口

实现位于 `Direct2dCad.Application.Tools`。终端、AI 和无界面宿主共用工具执行器，通过 `ICadToolWorkspace` / `ICadToolDocumentSession` 操作图纸；桌面 Dock 与文档生命周期适配保留在 ViewModels。详见[当前架构](ARCHITECTURE.md)与[本轮优化](ARCHITECTURE-OPTIMIZATION.md)。

“绘制”“修改”“标注”三个 Ribbon 页签只在 CAD 图纸上下文中显示。欢迎页等静态页面隐藏它们，并回到文件页；切回图纸恢复三个页签。点击图纸的属性或 Terminal 工具箱会保留当前图纸上下文，避免正常输入时隐藏工具。图纸恢复工具箱只提供恢复稿列表；绘制和标注的精确输入位于画布。

## Terminal 使用

`HELP` 显示基础命令和新增简写；`HELP MOVE` 或 `HELP M` 查看单个简写。`CADHELP` 只显示常用操作简写及 JSON 入口。`TOOLS` 列出所有工具，`TOOLS dimension` 按名称或说明筛选，`TOOLHELP set_dimension` 显示参数 schema。

JSON 工具既可以写成 `TOOL <名称> {JSON}`，也可直接写成 `<名称> {JSON}`，名称按目录规范化，大小写写法一致。`undo` / `redo` 与基础命令重名，JSON 调用必须加 `TOOL`。历史浏览与候选选择相互独立；输入时提供命令、子模式、资源候选及参数提示。

空 Enter 在绘图、夹点编辑或粘贴期间执行当前完成步骤，空闲时重复最近成功的可重复命令，不重复坐标、标量、DONE 或取消。`BACK` / `UNDOPOINT` 撤回上一未提交绘图点，`U` / `UNDO` 仍撤销文档操作。

后台命令执行期间，Esc / CANCEL 请求取消该次执行，终端等待任务退出后才接受下一条命令。切换图纸不会把取消重定向到新图纸；原图纸关闭或被替换后不再执行延迟的基础命令，已取消任务的迟到返回不会显示成功。无后台任务时，Esc 先关闭补全候选，再取消当前画布工具。完整行为见[交互优化说明](INTERACTION-OPTIMIZATION.md)。

基础命令分组：

| 类别 | 命令 |
| --- | --- |
| 帮助和状态 | `HELP`、`CLEAR`、`STATUS`、`RENDERSTATS` |
| 历史和视图 | `UNDO`、`REDO`、`FIT`、`ZOOM` |
| 选择和剪贴板 | `SELECT`、`SELECTALL`、`ERASE`、`COPY`、`PASTE` |
| 创建 | `LINE`、`CIRCLE`、`ARC`、`ELLIPSE`、`RECTANGLE`、`POLYLINE`、`POLYGON`、`SPLINE`、`TEXT` |
| 交互修改 | `OFFSET`、`TRIM`、`EXTEND`、`FILLET`、`CHAMFER`、`JOIN`、`BREAK`、`RECTARRAY`、`POLARARRAY` |
| 标注 | `DIMLINEAR`、`DIMVERTICAL`、`DIMALIGNED`、`DIMRADIUS`、`DIMDIAMETER`、`DIMANGULAR`、`LEADER` |
| 当前步骤 | `ORIGIN`、`MVIEW`、`DONE`、`BACK` / `UNDOPOINT`、`CANCEL` |

常用操作简写：

| 简写 | 示例 | 含义 |
| --- | --- | --- |
| `MOVE` / `M` | `MOVE 10,0` | 移动选中实体 |
| `ROTATE` / `RO` | `ROTATE 30 0,0` | 围绕指定基点旋转 30° |
| `SCALE` / `SC` | `SCALE 2 0,0` | 围绕指定基点等比缩放，比例必须为正 |
| `MIRROR` / `MI` | `MIRROR 0,0 90` | 以通过指定点、方向为 90° 的轴镜像 |
| `DUPLICATE` / `DUP` | `DUPLICATE 20,0` | 创建带偏移的副本并选中新实体 |
| `UNION` | `UNION` | 对至少两个选中闭合实体求并集 |
| `INTERSECT` | `INTERSECT` | 对选中闭合实体求交集 |
| `SUBTRACT` | `SUBTRACT 12` | 从选中的实体 12 中减去其他选中实体 |
| `NEW` | `NEW "Drawing A"` | 创建图纸 |
| `OPEN` | `OPEN "C:\Drawings\part.d2cad"` | 打开图纸 |
| `SAVE` | `SAVE "C:\Drawings\part.d2cad"` | 保存图纸；取消或失败不会返回成功 |
| `LAYER` | `LAYER` | 查看图层；支持新建、切换、修改等子命令，见 HELP LAYER |
| `DIST` | `DIST 0,0 30,40` | 点间距离 |
| `AREA` | `AREA 12` | 实体面积 |

这些简写也支持 `._MOVE` 等前缀。`COPY` / `CO` 保持原有剪贴板行为。变换的基点必须明确输入；差集必须明确指定要保留的主体，避免按选择顺序猜测。

**单位约定：**基础点输入和上述操作简写使用图纸当前显示单位；角度使用度，缩放比例无单位，实体 ID 是整数。JSON 工具的几何输入和输出始终使用毫米，与显示单位无关。Terminal 数字使用小数点 `.`。

## AI 工具覆盖与几何扩展

工具已覆盖工作区文件、批量创建、查询和测量、选择、变换、曲线修改与阵列、图层、块、外观、字体/样式、图像/OLE、视图和捕捉、DXF 以及历史。此前 2026-10-03 补齐的几何能力如下；本轮 12 个新增布局与视觉工具见[整改说明](COMMANDLINE-AND-AI-IMPLEMENTATION.md)。

| 工具 | 支持范围 |
| --- | --- |
| `boolean_regions` | 并集、交集、差集；明确输入实体 ID，差集指定主体；返回新 Region ID、面积和轮廓数 |
| `set_dimension` | 修改已有标注的位置、预设、字体、箭头、字号、箭头大小、延伸线、线宽、单位、精度、注释比例、线性标注旋转和替代文字 |
| `detach_dimension` | 解除一个明确标注的源几何关联，保留最后接受的基准点；撤销恢复关联 |
| `add_dimension`（扩展） | 创建时也可直接指定上述文字、箭头和样式参数；源实体与显式基准点二选一 |
| `get_entity_geometry`（扩展） | Region 返回直线、圆弧和椭圆弧轮廓；椭圆弧包含两个半轴、方向、起始参数角和扫角，`perimeter_approximate` 标明数值积分周长；Dimension 增加基准点引用、显示文字和线性旋转的读回 |
| `duplicate_entities`（修正） | 未提供 ID 时使用当前选择，与契约说明一致 |
| `transform_entities`（修正） | schema 正确说明椭圆、椭圆弧和矩形支持任意角度；OLE 保留既有旋转/镜像限制 |

Agent Contract 为 1.10，包含椭圆布尔及椭圆弧面域查询契约；能力分组从共享目录补齐，包含宿主渲染/打印能力标志。工具筛选支持组合意图、单位词、中文资源和追问，并可通过 discover_tools 补充工具；预算处理保留图像或明确报错。LM Studio 和 Codex 共用执行器及日志入口。

布尔调用示例，ID 应先由查询或选择结果取得：

```text
TOOL boolean_regions {"operation":"difference","entity_ids":[12,13],"subject_entity_id":12}
```

支持圆、完整圆弧、椭圆、完整椭圆弧、无圆角矩形、闭合直线/圆弧路径以及已有 Region（包括椭圆弧边界）。同一调用内的操作数必须位于当前可编辑空间，且图纸、实体和图层允许编辑。计算可取消；失败、空结果或取消不移除原实体。成功后原实体被替换为一个 Region，可含孔洞或分离部分，撤销恢复原实体。结果保留椭圆弧，不使用折线近似边界；数值交点不可靠时明确拒绝。样条和圆角矩形仍不能作为布尔操作数。详细精度及交换边界见[椭圆布尔与选择修复](SELECTION-AND-BOOLEAN-FIXES.md)。

标注调用示例：

```text
TOOL add_dimension {"kind":"Aligned","source_entity_id":12,"x":50,"y":20,"shape_font":"simplex","arrow":"Closed","text_height":3}
TOOL set_dimension {"entity_id":14,"x":50,"y":30,"arrow":"Slash","precision":3,"annotation_scale":2}
TOOL set_dimension {"entity_id":14,"text_override":null}
TOOL detach_dimension {"entity_id":14}
```

字体使用内置 ID：`unicode`、`simplex`、`monoline`、`box-fallback`；箭头使用 `Open`、`Closed`、`Slash`。替代文字前的 `*` 是原有测量警示；传入 `null` 恢复测量文字。属性更新保留基准点和关联；未知参数、非法尺寸、缺少成对的位置坐标、锁定对象或错误空间会失败，且不产生修改历史。

`document_id` 继续选择图纸；未指定时使用请求开始时的图纸，或工具最新创建/打开/激活的图纸。同一 AI 请求在每个图纸中分别形成撤销批次。JSON 变换等少数工具允许当前选择作为后备；布尔与标注编辑仍要求明确的 ID。

## 执行记录

此前 Terminal 已记录输入、返回值以及活动图纸的文档/编辑器命令。遗漏主要是 AI 查询、工具失败/取消和非活动图纸的执行记录。本次已补上：

| 来源 | Terminal 中的记录 |
| --- | --- |
| 手动 Terminal | 输入命令、返回值、错误；JSON 显示结构化结果，截图去掉 base64，只显示图像元数据与说明 |
| 文档/编辑器命令 | 执行、撤销、重做、批次数量及无修改结果，标明图纸名称；非活动图纸也记录 |
| 已有交互事件 | 模式切换、取消、平移结束、块编辑等，标明图纸名称 |
| AI 工具 | 工具名称、目标图纸、开始时的参数摘要、完成结果摘要、失败或取消；查询同样记录 |

AI 大数组和长文本只打印有界摘要；返回值仍提供给 AI，AI 面板保留工具结果摘要。连续平移中的每个视口采样不刷屏，结束时记录平移活动。鼠标移动、未提交预览和浏览设置页面本身不是文档修改命令。

Terminal 不是永久审计文件：保留最近 1000 行，待显示队列最多 4000 行，溢出会明确显示省略行数；使用现有分批刷新和虚拟化。已有打印结果不表示每种 UI 属性操作均已穷举，也不表示失败的鼠标预览会产生文档命令事件。

## 仍有边界

新增布局和视口生命周期、空间切换、纸张设置、标注重关联、capture_view 图像反馈及 print_document 预览入口，详见[12 个新增工具](COMMANDLINE-AND-AI-IMPLEMENTATION.md)。SCRIPT 可顺序执行文本命令、取消和首错停止，语法见[命令行说明](commandline-optimization/COMMANDLINE.md)。恢复稿管理仍使用 UI；Region 任意轮廓替换、任意曲线布尔、完整 DXF/DWG、参数约束和高级工程标注不因增加工具而自动获得支持。

本轮已在合成图纸上验证 Codex 与 LM Studio 的真实两轮创建、查询、修改及撤销重做。它证明本次具体任务链可执行，不代表任意复杂图纸的规划成功率；实际模型、端点与结果见[验证记录](commandline-ai-implementation/validation-2026-10-06/README.md)。
