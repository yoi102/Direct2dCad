# CAD 能力与不足

[返回首页](../README.md) · [开发规划与交互验收](ROADMAP.md) · [架构与依赖](ARCHITECTURE.md) · [性能与渲染](PERFORMANCE.md) · [测试说明](../scripts/testing/README.md)

历史审查日期：**2026-10-02**。代码基线：`041e291287e93429d49f498b5ca571f9a8f12427`。以下审查正文、探针和行号描述当时状态；源码行号会随实施移动。本次审查本身未修改业务代码。

## 2026-10-03 实施更新

M1–M6 的声明范围已在本地工作区实现，详情见 [M1–M3](M1-M3-STATUS.md)、[M4–M6](M4-M6-STATUS.md) 及 [最新验证](validation/2026-10-03/m4-m6/README.md)。本文保留原问题，供核对修复原因，不应继续把已修复项当作当前缺口。

| 历史问题 | 当前状态 |
| --- | --- |
| F1 精确坐标 / F2 原点测量 / F3 混合路径过滤 | 已修复，公共入口、历史与文件往返回归通过 |
| F4 打印后台结果 | 实现提交/完成/取消/失败反馈；实际打印机与纸面比例仍待验收 |
| G1 对象捕捉 / G2 基础编辑 / G6 方向变换 / G8 测量精度 | 已实现声明范围，含预览、失败回滚和历史；复杂曲线编辑仍有明确限制 |
| G7 操作工作流 | 三语言提示/HELP、画布动态输入、紧凑状态栏、工程模板与七种标注工作流完成；高级曲线完整动态参数、DPI/输入法人工验收保留 |
| R1 恢复 / R2 覆盖 / R3 预算 / R4 未知 section | 实现独立恢复、冲突选择、读取预算与只读兼容副本；恢复现场演练保留 |
| R6 查询/取消与环境基础 | 独立快照/版本分页/晚到拒绝、可见优先首屏、SDK/依赖锁和 CI、本机两模式三种样图；物理输入延迟仍未验收 |
| G3 标注 / G4 工程比例 / G5 外部交换 | 直接关联尺寸、actual/fit/custom 与共享驱动预览、有界 DXF 和独立外部往返完成；完整格式兼容与真实打印保留 |
| R5 容量 / R7 线程边界与分发 | 估计字节历史预算、另存压缩副本、worker 值快照及本机安装/升级/回退/卸载；chunk 仍关闭，签名/干净机器保留 |

新状态栏按用户反馈恢复横向样式，保留简短工具名、坐标/单位、网格类型/主次间距和带文字提示的捕捉/约束图标；步骤数字直接放在画布对应尺寸位置，光标旁的数字与半透明模式图标同行。“绘图辅助”仅保留恢复稿入口，没有恢复稿时启动收起。操作见[用户说明](DRAWING-AND-EDITING.md)及[动态输入验证](validation/2026-10-03/dynamic-input/README.md)。

## 历史审查结论

Direct2dCad 已经具备可用的二维编辑器基础：实体模型、图层/块、绘制与属性、撤销、布局视口、原生存储、打印、AI 工具及增量渲染有实际实现和回归测试。作为日常工程制图软件，主要差距集中在**精度可信、常用编辑、标注、外部交换、文档恢复和可复现交付**。

近期应先修复已经复现的坐标、测量和选择过滤缺陷，再补工程制图链路。继续优化渲染仍有价值，但不能替代精确坐标、尺寸标注、工程比例和图纸交换。

本文区分三种证据：

- **已复现缺陷**：运行真实业务程序集得到错误结果，给出输入和实际输出。
- **源码确认的行为或限制**：实现路径明确，但不把它等同于现场崩溃、性能超标或打印驱动验收失败。
- **产品能力缺口**：在当前实体、命令、工具、文件格式和界面入口中没有找到完整实现；这是下一阶段建设范围，不是已有功能的测试失败。

优先级：P0 为进入工程使用前应首先解决的数据精度问题；P1 为日常绘图或交付图纸的主要阻碍；P2 为容量、可靠性、性能及交付建设；P3 为随定位逐步完善的能力。优先级不是既定工期或发布承诺。

## 审查时能力与边界

| 能力 | 当前实现 | 需要注意的边界 |
| --- | --- | --- |
| 绘制 | 24 种绘制模式；直线、圆/圆弧、椭圆/椭圆弧、矩形、多段线、多边形、样条、文字；模型与工具另支持混合路径 | Polygon 是闭合 `CadPolyline`；混合路径的选择过滤接入有遗漏，见 F3 |
| 属性与外观 | 图层、颜色来源、线宽、描边端帽/连接/虚线、实色/渐变/Hatch；绘制默认值同步预览与创建 | 默认值按类型保留在文档会话，未承诺跨重启持久化；不适用属性按实体能力隐藏 |
| 几何编辑 | typed geometry、移动、正比例统一缩放、旋转、镜像、复制、grip、多实体属性 | 变换能力依赖实体类型；不是任意二维仿射变换，见 G6 |
| 输入与选择 | 点选、框选/跨选、候选切换、过滤、绝对/相对/极坐标输入、文档单位换算 | 鼠标吸附仅为网格吸附；Terminal 精确点提交有取整缺陷，见 F1 |
| 测量 | 距离、长度、角度、面积、交点、最近点/投影及弦长工具 | 高级交点/投影结果标记 `approximate`，普通长度/面积尚需误差预算；不是关联尺寸标注；原点缺陷见 F2 |
| 图层/块/布局 | 嵌套块引用、跨文档依赖导入、块编辑、纸空间和模型视口 | 已有 Layout 视口 `Scale`，最终打印仍会整纸 fit，见 G4 |
| 文件保存 | `.d2cad` 容器、section 版本与迁移、临时文件替换、保存排队、版本一致快照、取消保护 | 临时文件提交保护正常保存异常边界；不代表自动恢复、未知扩展保留或断电耐久性已验收 |
| 打印 | 预览、打印机/纸张/方向/份数/DPI、Windows XPS 矢量输出；图像/OLE 使用栅格内容 | 没有实际尺寸/指定比例选项；后台失败反馈不完整，见 F4 |
| 渲染与性能 | Direct2D 资源缓存、局部刷新、后台 geometry/LOD、设备失效重建、布局投影、基准程序 | 原生准备已有有界队列和消费预算；渐进首屏和某些后台录制线程边界仍待完善 |
| Terminal/AI | `HELP`、别名、历史、补全、工具 JSON；LM Studio/Codex 共用文档与实体工具 | 更多变换可通过 `TOOL` 调用，但没有完整 CAD 命令工作流；文件写入不属于图形 undo |

主要依据：[绘图模式](../Direct2dCad.ViewModels.Abstractions/Enums/CadCanvasToolMode.cs)、[实体模型](../Direct2dCad.Db/Data/Entities/)、[变换能力契约](../Direct2dCad.Commands/CadEntityTransformRules.cs)、[几何工具](../Direct2dCad.ViewModels/Tools/CadGeometryTools.cs)、[几何测量](../Direct2dCad.ViewModels/Tools/CadDocumentToolExecutor.cs)、[矢量打印](../Direct2dCad.wpf/Services/Printing/Vector/CadVectorPrintRenderer.cs)。

## 已确认的代码问题

### F1 · P0：Terminal 精确坐标在提交时被网格取整

复现：新建默认毫米图纸，依次执行 `LINE`、`0,0`、`1.25,2.75`。Terminal 返回 `Point accepted: 1.25,2.75`，但实际直线终点为 **(1, 3) mm**。把网格设为 `None` 或关闭吸附标记，仍得到相同结果。英寸图纸输入 `1,2` 的实际终点为 **(25, 51) mm**，期望为 **(25.4, 50.8) mm**。

原因：解析器已经完成单位换算，`SubmitDrawingPoint` 又无条件调用 `SnapWorld`。该服务按默认 1 mm 次网格 `Math.Round`，网格/标记是否显示不控制吸附。终端状态消息回报的是原解析值。

影响：文字输入和图形几何不一致，尺寸或非整数单位换算可能被静默改变。建议把输入来源分开：鼠标可按捕捉策略修正，显式精确坐标应保持输入；同时建立独立吸附开关和实际提交结果反馈。

验收：绝对、相对、极坐标，毫米/英寸及隐藏网格条件下，提交几何与输入一致；鼠标网格捕捉仍按开关工作。

源码：[提交点](../Direct2dCad.ViewModels/CadDocumentViewModel.cs#L2676)、[网格吸附](../Direct2dCad.ViewModels.Services/Snapping/CadSnapInteractionService.cs#L13)、[默认间距](../Direct2dCad.Db/Cad/Settings/CadGridSettings.cs#L40)、[返回消息](../Direct2dCad.CommandLine/CadCommandLineService.cs#L276)。

### F2 · P1：最近点/投影的合法原点结果被当成空结果

复现：直线从 `(-10,0)` 到 `(10,0)`，独立业务探针反射调用 `measure_geometry` 共用的底层 `ExecuteNearestPoint`，查询点为 `(0,0)`。该方法抛出 `The selected entities have no measurable segments.`；同一条线查询 `(1,0)` 正常返回距离 0。

原因：`FirstOrDefault()` 后用结构值 `nearest == default` 判断“没有候选”。合法投影 `Point=(0,0), DistanceSquared=0` 与默认结构值相同。`project_point` 共用该实现。公共工具 executor 会捕获该异常并返回失败结果，这一传播路径由源码确认；本次没有通过 UI/Terminal 执行测量。

建议改为显式检查候选存在性，再读取结果；验收覆盖原点、端点、在线上、线外以及真正没有可测线段的实体。

源码：[ExecuteNearestPoint](../Direct2dCad.ViewModels/Tools/CadDocumentToolExecutor.cs#L592)，错误判定在 604–606 行。

### F3 · P2：选择过滤“全关”仍允许 CompositePath

复现：图纸中加入普通 Line 和 CompositePath，禁用过滤目录中的全部类型，再调用真实 `SelectAllEntities`。Line 被过滤，**CompositePath 仍进入选择集**。

原因：混合路径已有模型、存储、工具和绘制支持，但 `CadSelectionEntityTypeCatalog` 未收录该类型；过滤器只禁用目录生成的类型，最终判断只检查具体实体类型是否在禁用集合里。

建议让可选实体目录覆盖全部支持类型，并新增“全关后无类型被选中”和新增实体跨模型/属性/过滤/存储/打印的接入检查。

源码：[类型目录](../Direct2dCad.ViewModels.Services/Interactions/CadSelectionEntityTypeCatalog.cs#L13)、[过滤面板](../Direct2dCad.ViewModels/Toolboxes/SelectionFilterToolboxViewModel.cs#L30)、[最终选择判定](../Direct2dCad.ViewModels/CadDocumentViewModel.cs#L1211)。

### F4 · P2：打印提交后的后台失败不向用户报告

源码确认：`PrintAsync` 启动作业后返回 `true`，界面显示“已提交”。随后 `WritingCompleted` 的错误/取消进入后台 completion task，但 `NotifyWhenPrintCompletesAsync` 在 `catch (Exception)` 中全部吞掉，既不成功通知，也不失败反馈。

界面已经区分提交与完成，缺少后续失败或取消反馈。建议向 UI 返回可观察的打印结果，分别表示提交、写入完成、取消和失败。实际打印机、驱动与 spooler 场景本次没有运行。

源码：[提交及后台通知](../Direct2dCad.wpf/Services/Printing/CadPrintService.cs#L54)、[异常处理](../Direct2dCad.wpf/Services/Printing/CadPrintService.cs#L224)、[界面反馈](../Direct2dCad.ViewModels/EditorTabViewModel.cs#L485)。

## 工程制图的主要能力缺口

| 编号 / 优先级 | 当前差距与依据 | 建议先完成的范围 | 可验证的完成标准 |
| --- | --- | --- | --- |
| G1 / P1：对象捕捉与精确输入 | [SnapWorld](../Direct2dCad.ViewModels.Services/Snapping/CadSnapInteractionService.cs#L13)只查询原点与网格，不查询实体；未找到端点、中点、圆心、交点、垂足、相切捕捉和正交/极轴跟踪 | 先修 F1，再建立捕捉开关、屏幕容差、候选优先级及提示；第一批做端点/中点/圆心/交点 | 缩放、单位切换、嵌套块变换和近邻候选下仍落到正确模型坐标 |
| G2 / P1：工程编辑命令 | [实体命令](../Direct2dCad.Commands/)、[Terminal 目录](../Direct2dCad.CommandLine/CadCommandLineService.cs#L45)和[工具目录](../Direct2dCad.ViewModels/Tools/CadGeometryTools.cs)有绘制和基础变换，未找到完整 Trim/Extend/Offset/Fillet/Chamfer/Join/Break/Array 工作流 | 按直线/圆弧/多段线逐步建设偏移、修剪、延伸，再补圆角/倒角和拓扑编辑；与预览、撤销、属性、存储同步接入 | 多交点选择、闭合方向、零长段、极小半径、取消/失败回滚及重做保持几何一致 |
| G3 / P1：尺寸标注与工程注释 | [实体模型](../Direct2dCad.Db/Data/Entities/)有普通文字和测量工具，未找到 Dimension/Leader/DimStyle 或几何关联更新 | 先做线性、对齐、角度、半径/直径标注和引线；建立尺寸样式、单位、精度与纸空间比例规则 | 改动被标注图形后标注跟随更新，复制/块/保存重开/打印后不失关联 |
| G4 / P1：实际尺寸及指定比例打印 | [ResolvePageMetrics](../Direct2dCad.wpf/Services/Printing/CadPrintService.cs#L383)始终 fit 到可打印矩形，[矢量输出](../Direct2dCad.wpf/Services/Printing/Vector/CadVectorPrintRenderer.cs#L34)再缩放整纸；[打印选择](../Direct2dCad.wpf/Views/Dialogs/CadPrintPreviewDialog.xaml.cs#L93)没有实际尺寸/指定比例。Layout 视口虽有 Scale，整纸会再次缩放 | 明确“实际尺寸 / 指定比例 / 适合纸张”；按 mm 与 WPF DIP 换算，保留纸张物理尺寸和裁剪提示 | 输出 100 mm 校准线和 1:100 图样，用 PDF/实体打印测量验证；检查可选择文字、线宽及边距 |
| G5 / P1：文件交换 | [文件对话框](../Direct2dCad.wpf/Services/Importing/FileDialogService.cs#L12)只打开/保存 `.d2cad`；未找到 DXF/DWG 实体交换实现。PDF 驱动、AI SVG 附件不等于 CAD 格式转换 | 优先做 DXF 读写，声明支持实体、图层、块、文字、单位、线型与未知对象策略；DWG 另评估 SDK/许可 | 用固定外部 CAD 样本双向往返，检查几何、单位、图层/块与文字；明确报告未支持内容 |
| G6 / P2：曲线模型与变换覆盖 | [变换契约](../Direct2dCad.Commands/CadEntityTransformRules.cs#L30)限制椭圆/矩形为 90°旋转、45°倍数镜像轴；椭圆弧拒绝旋转/缩放/镜像。[样条](../Direct2dCad.Db/Data/Entities/CadSpline.cs)是拟合点插值，不是完整 NURBS 数据模型 | 为实体建立方向或受控转为混合路径；工程曲线运算制定模型容差和误差报告，随后考虑弧段多段线/NURBS | 任意支持角度下 bounds、命中、grip、复制、存储、打印一致；不能只放宽入口 guard |
| G7 / P3：约束、模板和用户工作流 | 未找到完整几何/尺寸约束求解、关联设计或工程模板体系；[Terminal 帮助](../Direct2dCad.CommandLine/CadCommandLineService.cs#L47)及提示仍大量硬编码英文，圆弧子命令缺完整输入示例 | 先补用户命令参考、模板和工程样式；是否做参数约束根据机械草图或一般二维制图定位决定 | 常用任务能按文档独立完成；命令提示与中/日/英 UI 一致；约束变更能明确报告冲突 |
| G8 / P2：工程测量误差预算 | [样条测长](../Direct2dCad.Db/Data/Entities/CadSpline.cs#L7)和[曲线离散](../Direct2dCad.ViewModels/Tools/CadDocumentToolExecutor.cs#L682)使用固定采样；高级结果有近似标记，普通长度/面积没有统一误差说明 | 直线、圆、圆弧尽量解析计算；复杂曲线用可配置公差的自适应算法，并返回近似/误差元数据 | 小尺寸、高曲率、大坐标和相切样本有可核查误差，避免将采样结果当成工程精确值 |

二维 CAD 的近期重点是准确绘图、标注和交付；3D 建模、装配等属于另外的产品范围，不因缺少它们就否定当前二维路线。

## 保存、容量和运行可靠性

### R1 · P1：自动保存、恢复与备份尚未形成

已有正常关闭的未保存提醒、取消不替换原文件、同目录临时文件提交及保存版本基线。未找到周期恢复副本、启动恢复列表或历史备份服务；临时保存文件结束后被清理，不能当成崩溃恢复机制。

建议保留独立恢复副本和受限备份轮换，测试崩溃、磁盘满、取消和网络路径。恢复副本不能在用户确认前覆盖正式图纸。同步保存有 `Flush(flushToDisk:true)`，异步路径只 `FlushAsync`；断电持久性本次未验证。

依据：[保存会话](../Direct2dCad.ViewModels.Services/Documents/CadDocumentSaveSession.cs#L49)、[存储写入](../Direct2dCad.IO/CadDocumentStorage.cs#L85)、[临时文件提交/清理](../Direct2dCad.IO/CadDocumentStorage.cs#L464)。

### R2 · P1：工具指定已有目标路径时缺独立覆盖保护

工具 `save_document` 传入 `file_path` 时直接走保存方法，最终 `File.Move(..., overwrite:true)`，不经过 SaveAs 对话框的覆盖确认。正常保存当前文件当然需要覆盖；风险点是工具提供了另一个已存在目标，现有路径没有独立冲突确认或备份。

源码已确认这条路径，本次未覆盖任何真实文件。图形修改进入 undo，磁盘文件覆盖不属于图形 undo。建议给新路径覆盖增加明确策略，并区分 AI 查询、预览和编辑能力。现有关闭确认及 Codex shell 只读 sandbox 不能覆盖动态 CAD 工具的这条写入路径。

依据：[workspace 保存](../Direct2dCad.ViewModels/Tools/CadToolWorkspace.cs#L147)、[直接保存入口](../Direct2dCad.ViewModels/EditorTabViewModel.cs#L134)、[最终替换](../Direct2dCad.IO/CadDocumentStorage.cs#L472)。

### R3 · P1：外部文件的内存/解压预算不明确

容器已有最多 4096 sections、边界、重复及重叠校验，这是已有保护。[MessagePack 选项](../Direct2dCad.IO/CadDocumentStorage.cs#L12)仍为 Standard；[读取 payload](../Direct2dCad.IO/CadDocumentStorage.cs#L163)按 section 长度分配数组，未设置单 section、总解压大小、实体数量或嵌入内容预算。

建议给不可信文件定义明确上限、采用相应反序列化安全选项并测试超限/解压膨胀。这里确认的是资源防护缺口，没有制造 OOM，也没有据此声称存在已利用漏洞。

### R4 · P2：未知 section 的打开/另存策略会丢失扩展内容

在支持的容器版本内，读取层接受未知 section payload，加载模型只映射已知 sections，重新保存只生成已知列表。因此未知扩展不能往返保留。已知 section 的不支持高版本会明确拒绝，这是另一条正确边界。

建议原样保留未知 sections，或明确只读打开/拒绝重写；加入“带未知扩展的文件打开后另存”回归。源码路径确认，本次未运行专门的扩展文件探针。

依据：[读取列表](../Direct2dCad.IO/CadDocumentStorage.cs#L157)、[加载映射](../Direct2dCad.IO/CadDocumentStorage.cs#L238)、[保存列表](../Direct2dCad.IO/CadDocumentStorage.cs#L389)。

### R5 · P2：软删除、撤销与大型嵌入内容缺少统一容量策略

删除命令只 `Erase()`，保存仍收集所有实体并持久化 `IsErased`；重开仍恢复已擦除对象。未找到 Purge/Compact 路径，撤销历史裁剪只移除历史项，不回收模型中的已擦除实体。删除大型 Image/OLE 后，数据仍可能留在模型和保存文件中。

历史条数软上限已有实现，但默认 0（不限制），最新完整批次可以超限；大型 OLE 更新还持有完整前后数据。应建立安全清理、存储压缩及按字节估计的历史预算，并明确清理对 redo/引用的影响。本次没有测量长期图纸增长量。

依据：[删除](../Direct2dCad.Commands/DeleteEntitiesCommand.cs#L27)、[全部实体索引](../Direct2dCad.IO/CadDocumentMapper.cs#L22)、[IsErased 存储](../Direct2dCad.IO/CadDocumentMapper.cs#L1226)、[历史裁剪](../Direct2dCad.Editor/History/CommandHistory.cs#L29)、[容量设置](../Direct2dCad.Editor/History/CommandHistorySettings.cs#L10)、[OLE 数据命令](../Direct2dCad.Commands/SetOleObjectDataCommand.cs#L24)。

<a id="large-drawings"></a>

### R6 · P2：大图纸加载、首屏与查询仍有同步工作边界

- 加载先持有全部 section payload，再解压 DTO 并构建实体，峰值内存可能叠加。解码与模型映射已通过 `Task.Run` 执行，但映射内部没有协作取消，GUI 打开入口未传入取消令牌。
- 首次 Present 已支持可见区域及嵌套定义就绪即显示；等待集合随模型视区及 Layout 模型视口更新。完整准备继续后台执行，单个巨大实体操作仍无法由时间预算抢占。
- AI 分页控制返回量，查询仍遍历作用域实体计数/比较；高 offset 保留 `offset + limit` 候选。工具在 UI 同步上下文中执行，同步遍历没有协作取消。

建议继续以固定真实图纸测峰值内存、物理输入 P95 延迟及取消响应，再做 section 流式解码和版本化查询索引/游标分页。已有可见优先首屏、原生准备队列上限、约 2 ms 消费预算和增量索引，不能把这些已完成措施重新列为缺失。2026-10-04 两份真实图纸的原生首屏及绘制对照见[验证记录](validation/2026-10-04/render-optimization/README.md)，尚未测量物理输入和 WPF 合成延迟。

依据：[加载](../Direct2dCad.IO/CadDocumentStorage.cs#L157)、[打开入口](../Direct2dCad.ViewModels/MainViewModel.cs#L227)、[首次 Present](../Direct2dCad.Rendering.Direct2D/Hosting/Direct2DImageRenderHost.cs#L742)、[查询遍历](../Direct2dCad.ViewModels/Tools/CadEntityQuery.cs#L72)、[分页](../Direct2dCad.ViewModels/Tools/CadEntityQuery.Paging.cs#L17)、[UI 工具分发](../Direct2dCad.Agent.Codex/CodexAppServerClient.cs#L572)。

<a id="background-recording"></a>

### R7 · P2：可选后台 chunk 录制仍读取可变模型

原生 geometry 准备已有值快照，但另一条后台 chunk 录制路径保存 live document/entity 引用，并在 worker 绘制时读取几何；命令修改实体后发布变更，后续缓存处理才取消/等待录制。修改和后台读取之间存在相交窗口。

这是源码线程设计风险，**没有复现错图或崩溃**。该优化默认关闭，且已有 generation 废弃结果、共享资源修改前等待和前台 fallback。建议 worker 全部使用独立值快照，或在实体修改前建立统一暂停边界，并补重叠执行压力测试。

依据：[worker 引用](../Direct2dCad.Rendering.Direct2D/Scene/Direct2DChunkRecordingWorker.cs#L132)、[worker 消费](../Direct2dCad.Rendering.Direct2D/Scene/Direct2DChunkRecordingWorker.cs#L254)、[绘制读实体](../Direct2dCad.Rendering.Direct2D/Entities/Direct2DEntityRenderer.cs#L295)、[命令执行顺序](../Direct2dCad.Editor/Commands/CadDocumentCommandManager.cs#L46)、[缓存取消](../Direct2dCad.Rendering.Direct2D/Scene/Direct2DCommandListChunkCache.cs#L341)、[默认设置](../Direct2dCad.Client.Common/Settings/CadUserSettings.cs#L124)。

## 架构与发布维护

- `CadDocument`、命令、变更描述、存储和渲染项目分层较清楚；保存会话、事务回滚、版本化缓存与针对生命周期的回归是可保留的基础。不能因为还有缺口就推翻这套架构。
- ViewModels 与 Services 仍引用 Direct2D，`CadDocumentViewModel` 直接持有渲染宿主，跨 UI/后端迁移还需要服务接口。当前是 Windows WPF 实现；解决方案内空的 Avalonia 分组不等于已有跨平台客户端。参见[实际依赖表](ARCHITECTURE.md#项目引用表)。
- `0.0.0.2` 已发布 GitHub，提供 Windows x64 自包含 MSI 与 ZIP；SDK、依赖锁文件、Windows MSI 工程和 SHA256 清单已纳入仓库。部分 UI/DI 依赖仍为 preview/rc/ci。
- 此次 MSI 未签名，也未在干净机器实装、升级和卸载验收；托管 CI 未运行。文件版本迁移政策需要维护，外部 OLE、真实打印机、DWG 与完整 DXF 能力仍需持续专项验收。
- 下一阶段关注干净 Windows 安装/升级回归、代码签名，以及打印、外部 OLE 等机器相关能力。

依据：[WPF 构建与版本](../Direct2dCad.wpf/Direct2dCad.wpf.csproj#L10)、[回归脚本的可选集成范围](../scripts/testing/Run-Regression.ps1#L28)。

## 建议的推进顺序

具体任务、交互规则、依赖和验收标准见 [开发规划与交互验收](ROADMAP.md)。下表保留本次审查的概括；实施顺序与任务状态以后续规划和实际证据为准。

| 阶段 | 优先处理 | 退出条件 |
| --- | --- | --- |
| 1：保证输入和数据可信 | F1 精确坐标、F2 原点测量、F3 过滤接入；R1 恢复副本、R2 覆盖保护、R3 文件预算 | 精确输入贯穿 UI/Terminal/工具/保存；重要文件异常不静默损失；专项回归可重复运行 |
| 2：完成常用二维绘图链路 | G1 对象捕捉、G2 修剪/延伸/偏移、G3 尺寸标注、G6 关键变换 | 固定样图从绘制到改图、标注、保存重开能完整完成，取消/撤销/重做一致 |
| 3：完成图纸交付 | G4 工程比例打印、F4 后台打印结果、G5 DXF 交换、R4 扩展兼容 | 外部 CAD 往返样本和物理尺寸校准通过，有不支持内容报告 |
| 4：容量与正式发布 | R5 清理/历史预算、R6 大图纸响应、R7 worker 边界、签名和干净机器安装验收 | 固定机器/图纸的内存和延迟阈值达标，签名后的安装、升级和卸载经干净机器实测 |

对象捕捉与基本编辑属于普遍二维 CAD 工作流；约束求解、动态块、外部参照或三维能力应另按目标用户确定范围，避免先把项目扩大成没有边界的功能集合。

## 本次验证

执行：

```powershell
.\scripts\testing\Run-Regression.ps1 -Configuration Release -ResultsDirectory .\TestResults\review-2026-10-02
```

- 本机选中 SDK：`11.0.100-preview.5.26302.115`，项目目标仍是 `.NET 10`。
- Release 解决方案构建：**0 警告、0 错误**。
- 默认 12 个托管回归项目：**1197 / 1197 通过，0 失败，0 跳过**。
- 覆盖率汇总器自检通过；本次未加 `-CollectCoverage`，没有新的代码覆盖率百分比。
- F1、F2、F3 使用已构建 Release 业务程序集及现有测试上下文做独立探针复现，探针没有修改业务或测试源码。
- 文档本地链接、源码行引用、Markdown 结构和 `git diff --check` 在整理后单独检查。

| 项目 | 通过用例 |
| --- | ---: |
| Agent.Codex | 19 |
| Agent | 7 |
| AI | 21 |
| Commands | 61 |
| Db | 71 |
| Editor | 65 |
| HitTesting | 18 |
| Indexing | 18 |
| IO | 32 |
| 跨层 Tests | 96 |
| ViewModels.Services | 73 |
| ViewModels | 716 |
| **合计** | **1197** |

原始 TRX 在本机 `TestResults/review-2026-10-02/<项目>/`，该目录按仓库规则不提交。辅助探针位于本机 `TestResults/review-2026-10-02/cad-review-probe/`；以上输入、输出及源码原因在本文保留，不依赖临时目录才能理解。

本次未运行 Windows/Direct2D 集成测试、独立应用 UI 自动化、真实打印机/PDF 驱动、Word/Excel 外部 OLE、多 GPU/混合 DPI、长时间大图纸压力或新性能基准。现有这些测试的源码和历史记录可查，但不计入本次通过数。默认回归全部通过与新增探针发现缺陷并不矛盾：原测试尚未覆盖这三个场景。
