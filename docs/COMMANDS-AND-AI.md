# 命令行与 AI 工具

[返回首页](../README.md) · [绘图与编辑](DRAWING-AND-EDITING.md) · [标注与交换](ANNOTATION-AND-EXCHANGE.md) · [本次验证](validation/2026-10-03/command-coverage/README.md)

2026-10-03 核对运行时目录：42 个基础命令、8 个常用操作简写、89 个 Terminal / AI 共用工具。别名和同一命令的几何变体不重复计数；这三类目录有功能重叠，数量不代表完整 CAD 能力。完整名称、描述和 JSON schema 见[运行时命令目录](validation/2026-10-03/command-coverage/command-catalog.json)。

## 页面和入口

“绘制”“修改”“标注”三个 Ribbon 页签只在 CAD 图纸上下文中显示。欢迎页等静态页面隐藏它们，并回到文件页；切回图纸恢复三个页签。点击图纸的属性或 Terminal 工具箱会保留当前图纸上下文，避免正常输入时隐藏工具。图纸恢复工具箱只提供恢复稿列表；绘制和标注的精确输入位于画布。

## Terminal 使用

`HELP` 显示基础命令和新增简写；`HELP MOVE` 或 `HELP M` 查看单个简写。`CADHELP` 只显示常用操作简写及 JSON 入口。`TOOLS` 列出所有工具，`TOOLS dimension` 按名称或说明筛选，`TOOLHELP set_dimension` 显示参数 schema。

JSON 工具既可以写成 `TOOL <名称> {JSON}`，也可直接写成 `<名称> {JSON}`。`undo` / `redo` 与基础命令重名，JSON 调用必须加 `TOOL`。补全、上下键历史、Tab 和取消沿用现有 Terminal 行为。

基础命令分组：

| 类别 | 命令 |
| --- | --- |
| 帮助和状态 | `HELP`、`CLEAR`、`STATUS`、`RENDERSTATS` |
| 历史和视图 | `UNDO`、`REDO`、`FIT`、`ZOOM` |
| 选择和剪贴板 | `SELECT`、`SELECTALL`、`ERASE`、`COPY`、`PASTE` |
| 创建 | `LINE`、`CIRCLE`、`ARC`、`ELLIPSE`、`RECTANGLE`、`POLYLINE`、`POLYGON`、`SPLINE`、`TEXT` |
| 交互修改 | `OFFSET`、`TRIM`、`EXTEND`、`FILLET`、`CHAMFER`、`JOIN`、`BREAK`、`RECTARRAY`、`POLARARRAY` |
| 标注 | `DIMLINEAR`、`DIMVERTICAL`、`DIMALIGNED`、`DIMRADIUS`、`DIMDIAMETER`、`DIMANGULAR`、`LEADER` |
| 当前步骤 | `ORIGIN`、`MVIEW`、`DONE`、`CANCEL` |

新增操作简写直接处理当前选择：

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

这些简写也支持 `._MOVE` 等前缀。`COPY` / `CO` 保持原有剪贴板行为。变换的基点必须明确输入；差集必须明确指定要保留的主体，避免按选择顺序猜测。

**单位约定：**基础点输入和上述操作简写使用图纸当前显示单位；角度使用度，缩放比例无单位，实体 ID 是整数。JSON 工具的几何输入和输出始终使用毫米，与显示单位无关。Terminal 数字使用小数点 `.`。

## AI 工具覆盖和本次补充

原有工具已覆盖工作区文件、批量创建、查询和测量、选择、变换、曲线修改与阵列、图层、块、外观、字体/样式、图像/OLE、视图和捕捉、DXF 以及历史。本次新增 3 个工具，并扩展相关接口：

| 工具 | 支持范围 |
| --- | --- |
| `boolean_regions` | 并集、交集、差集；明确输入实体 ID，差集指定主体；返回新 Region ID、面积和轮廓数 |
| `set_dimension` | 修改已有标注的位置、预设、字体、箭头、字号、箭头大小、延伸线、线宽、单位、精度、注释比例、线性标注旋转和替代文字 |
| `detach_dimension` | 解除一个明确标注的源几何关联，保留最后接受的基准点；撤销恢复关联 |
| `add_dimension`（扩展） | 创建时也可直接指定上述文字、箭头和样式参数；源实体与显式基准点二选一 |
| `get_entity_geometry`（扩展） | Region 返回精确直线/圆弧轮廓；Dimension 增加基准点引用、显示文字和线性旋转的读回 |
| `duplicate_entities`（修正） | 未提供 ID 时使用当前选择，与契约说明一致 |
| `transform_entities`（修正） | schema 正确说明椭圆、椭圆弧和矩形支持任意角度；OLE 保留既有旋转/镜像限制 |

Agent Contract 更新到 1.8，包含 Region、Dimension 的能力和示例。工具筛选补入布尔、标注、曲线编辑、阵列和 DXF 意图，优先保留用户要执行的操作；相关工具通过默认 8192 上下文预算检查。LM Studio 和 Codex 共用执行器及日志入口。

布尔调用示例，ID 应先由查询或选择结果取得：

```text
TOOL boolean_regions {"operation":"difference","entity_ids":[12,13],"subject_entity_id":12}
```

支持圆、完整圆弧、无圆角矩形、闭合直线/圆弧路径以及已有 Region。同一调用内的操作数必须位于当前可编辑空间，且图纸、实体和图层允许编辑。计算可取消；失败、空结果或取消不移除原实体。成功后原实体被替换为一个 Region，可含孔洞或分离部分，撤销恢复原实体。椭圆、样条和圆角矩形不能作为布尔操作数。

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
| 手动 Terminal | 输入命令、返回值、错误；JSON 返回完整结果 |
| 文档/编辑器命令 | 执行、撤销、重做、批次数量及无修改结果，标明图纸名称；非活动图纸也记录 |
| 已有交互事件 | 模式切换、取消、平移结束、块编辑等，标明图纸名称 |
| AI 工具 | 工具名称、目标图纸、开始时的参数摘要、完成结果摘要、失败或取消；查询同样记录 |

AI 大数组和长文本只打印有界摘要；返回值仍提供给 AI，AI 面板保留工具结果摘要。连续平移中的每个视口采样不刷屏，结束时记录平移活动。鼠标移动、未提交预览和浏览设置页面本身不是文档修改命令。

Terminal 不是永久审计文件：保留最近 1000 行，待显示队列最多 4000 行，溢出会明确显示省略行数；使用现有分批刷新和虚拟化。已有打印结果不表示每种 UI 属性操作均已穷举，也不表示失败的鼠标预览会产生文档命令事件。

## 仍有边界

这些接口足以覆盖项目当前的大部分常用绘图和修改任务，但没有完整替代成熟 CAD 的命令系统。布局/模型视口对象的生命周期、恢复稿管理、打印设置，以及现有标注重新关联，尚无独立 AI 工具；对应的现有 UI 入口仍可使用。Region 任意轮廓替换、任意曲线布尔、完整 DXF/DWG、参数约束和高级工程标注也不在本次范围。

本次验证针对本机工具执行、协议、上下文筛选和真实 WPF 窗口。没有通过真实 LM Studio 模型或在线 Codex 会话验收模型理解与多轮规划质量。
