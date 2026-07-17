# Direct2dCad

## 项目简介

Direct2dCad 是一个基于 WPF、Direct2D 和 DirectWrite 的桌面 CAD 编辑器项目，用于实现并验证可维护的 CAD 编辑架构与高性能渲染方案。

主要功能：

- 绘制和编辑常见 CAD 实体，支持图层、样式、填充、文字、OLE对象与图像。
- 提供选择、框选、grip / handle 拖拽、跨文档复制粘贴和多实体编辑；复制 Block Reference 时会递归携带依赖的块定义。
- 使用命令系统管理文档与视口操作，支持单条或批量 undo / redo。
- 通过 Direct2D 资源缓存、局部刷新和变更跟踪提高渲染效率。
- 提供 WPF 工具面板、属性设置、文件读写、CAD Terminal 和多语言界面。

## 演示与设计

- [基本操作演示]
  
https://github.com/user-attachments/assets/53180795-5870-42c7-9148-5586ca1bfd6b


https://github.com/user-attachments/assets/5515d18a-1d88-4851-a8d9-54f10bdee5ed

- [Block 演示]
  
https://github.com/user-attachments/assets/45c5e49e-c59a-4f80-aaf3-de8ec7680310

- [Layout 演示]

https://github.com/user-attachments/assets/847600ec-c82e-4ed0-82d9-443d59339906

- [OLE 对象演示]

https://github.com/user-attachments/assets/ab1f207f-48c2-40a8-b698-496c6077a0a3

- [Terminal 演示]

https://github.com/user-attachments/assets/fc7236e2-93e8-44f3-800d-b00bfd54f761
- [Figma 设计稿](https://www.figma.com/board/wZWqWgQ9dd1p4KQVBakqmS/Direct2dCad?node-id=52-299&t=jXGAkAOnYQmodsTk-4)





## 项目组成

| 分层 | 项目 |
|---|---|
| 核心编辑 | `Direct2dCad.Db`, `Direct2dCad.ChangeTracking`, `Direct2dCad.Commands`, `Direct2dCad.CommandLine`, `Direct2dCad.Editor` |
| 查询与存储 | `Direct2dCad.HitTesting`, `Direct2dCad.Indexing`, `Direct2dCad.IO` |
| 渲染 | `Direct2dCad.Rendering`, `Direct2dCad.Rendering.Transient`, `Direct2dCad.Rendering.Handles`, `Direct2dCad.Rendering.Direct2D` |
| 客户端公共能力 | `Direct2dCad.Client.Common`, `Direct2dCad.Lang` |
| ViewModel | `Direct2dCad.ViewModels.Abstractions`, `Direct2dCad.ViewModels.Services`, `Direct2dCad.ViewModels` |
| WPF | `Direct2dCad.wpf.Controls`, `Direct2dCad.wpf` |

`Direct2dCad.ViewModels.Services` 保存平台接口和 UI 无关的交互协作者；WPF 实现位于 `Direct2dCad.wpf/Services`，跨 ViewModel 通信使用 MessagePipe。

## 架构分层

```mermaid
flowchart TD
    UI["WPF UI<br/>Direct2dCad.wpf<br/>Direct2dCad.wpf.Controls"]
    VMAbs["VM Abstractions<br/>Direct2dCad.ViewModels.Abstractions"]
    VMServices["VM Services<br/>Direct2dCad.ViewModels.Services"]
    VM["ViewModels<br/>Direct2dCad.ViewModels"]
    Client["Client Common / Lang<br/>Direct2dCad.Client.Common<br/>Direct2dCad.Lang"]
    Editor["Editor<br/>Direct2dCad.Editor"]
    Commands["Commands<br/>Direct2dCad.Commands"]
    CommandLine["Command Line<br/>Direct2dCad.CommandLine"]
    ChangeTracking["Change Tracking<br/>Direct2dCad.ChangeTracking"]
    Db["CAD Data Model<br/>Direct2dCad.Db"]
    Query["HitTesting / Indexing<br/>Direct2dCad.HitTesting<br/>Direct2dCad.Indexing"]
    Rendering["Rendering Abstractions<br/>Direct2dCad.Rendering"]
    Transient["Transient Scene<br/>Direct2dCad.Rendering.Transient"]
    Handles["Handle Scene<br/>Direct2dCad.Rendering.Handles"]
    Direct2D["Direct2D Backend<br/>Direct2dCad.Rendering.Direct2D"]
    IO["Persistence<br/>Direct2dCad.IO"]

    UI --> VM
    UI --> VMAbs
    UI --> CommandLine
    VM --> VMAbs
    VM --> CommandLine
    VM --> VMServices
    VM --> Client
    VM --> Editor
    VM --> IO
    VM --> Direct2D
    VMServices --> Editor
    VMServices --> Rendering
    VMServices --> Direct2D
    VMServices --> Handles
    VMServices --> Transient
    Editor --> Commands
    Editor --> ChangeTracking
    Editor --> Query
    Editor --> Rendering
    Commands --> ChangeTracking
    Commands --> Db
    ChangeTracking --> Db
    Query --> Db
    Rendering --> ChangeTracking
    Rendering --> Db
    Transient --> Db
    Handles --> Db
    Direct2D --> Rendering
    Direct2D --> Handles
    Direct2D --> Transient
    Direct2D --> ChangeTracking
    Direct2D --> Db
    IO --> Db
    Client --> Db
```

## 项目职责

### Direct2dCad.Db

核心 CAD 数据模型层，是图纸内容的 source of truth。

主要职责：

- 定义 `CadDocument`、Layer、Block、Entity、Style、FillStyle、HatchPattern 等核心模型。
- 定义 line、circle、arc、ellipse、ellipse arc、rectangle、polyline、spline、text、shape text、block reference 等实体。
- 定义文档级 `CadViewSettings`、grid、origin、layer drawing priority 等设置。
- 定义 `CadPointD`、`CadVectorD`、`CadRectD`、`CadMatrixD` 等几何类型。
- 定义 `EntityId`、`LayerId`、`BlockId`、`StyleId` 等强类型 ID。

原则：这里不依赖 editor、rendering、WPF，也不直接关心 Direct2D 资源。

### Direct2dCad.ChangeTracking

CAD 文档变更描述层。

主要职责：

- 定义 `CadDocumentChangeSet`。
- 定义实体、文档结构、视图设置等变更范围。
- 区分 geometry、appearance、fill、visibility、layer、draw order 等变更类型。
- 作为 Commands、Editor、Indexing、Rendering 之间的中性通知模型。
- 避免 `Direct2dCad.Rendering` / `Direct2dCad.Rendering.Direct2D` 直接依赖 `Direct2dCad.Commands`。

### Direct2dCad.Commands

CAD 文档命令层。

主要职责：

- 定义 `ICadCommand` 和命令执行结果。
- 实现实体 CRUD、属性修改、图层修改、原点设置等可 undo / redo 的文档命令。
- 使用统一的剪贴板快照实现复制、粘贴和重复实体，支持嵌套 Block Reference 及其依赖资源。
- 支持单条命令和批量命令。
- 批量命令是否按组 undo / redo，应该由命令管理设置决定，而不是由渲染层决定。
- 命令执行后返回 `CadDocumentChangeSet`，用于索引、缓存和 Direct2D 资源更新。

### Direct2dCad.CommandLine

与 UI 无关的 CAD 命令行协议与解析层。

主要职责：

- 定义命令目录、语法、别名、执行上下文和执行结果。
- 通过 `ICadCommandLineHandler` 和 `CadCommandLineRegistry` 注册内置、插件或 AI 命令，无需修改中心 switch。
- 支持 `HELP`、undo / redo、fit、选择、删除、复制粘贴以及实体绘制模式命令；复制粘贴结果会报告实体、块引用和依赖块定义数量。
- 支持 Tab 补全、命令历史、空 Enter 重复命令，以及 `X,Y`、`@dX,dY`、`@距离<角度` 坐标输入。
- 将圆、圆弧、椭圆等命令的子模式转换为稳定的语义枚举。
- 不依赖 WPF、ViewModels、Editor 或 Db，可供桌面 UI、脚本、插件和后续 AI 功能复用。

WPF Terminal 的日志、输入历史和当前文档适配仍由 ViewModel 层负责；真实的文档和视口操作继续进入 Editor 命令系统。

### Direct2dCad.Editor

编辑应用层，协调文档、命令、选择、命中测试、索引、视口和渲染资源更新。

主要职责：

- 提供 `CadEditor` 作为编辑入口。
- 管理文档命令执行、undo、redo。
- 维护选择集。
- 连接 hit testing 和 spatial index。
- 发布 `CadDocumentChangeSet`。
- 根据实体变更通知 `ICadGeometryResourceManager` 更新或释放 geometry / brush / text 等资源。
- 提供 pan、zoom、fit 等视口命令。

### Direct2dCad.HitTesting

命中测试层。

主要职责：

- 在 CAD 世界坐标下执行点选、框选、反选候选判断。
- 命中测试需要考虑实体几何、line weight、文本外框或填充规则、block reference 变换等。
- 返回候选实体和命中信息，供 Editor / ViewModels 决定选择行为。

### Direct2dCad.Indexing

空间索引层。

主要职责：

- 记录实体 bounds。
- 按区域查询候选实体。
- 为框选、命中测试、局部刷新提供候选集合。
- 当实体 geometry / line weight / fill / visibility / layer 等影响 bounds 或可见性的属性改变时，需要通过变更通知更新索引。

### Direct2dCad.Rendering

渲染抽象层，不绑定具体 Direct2D 后端。

主要职责：

- 定义 `ICadRenderer`。
- 定义 `ICadGeometryResourceManager`。
- 定义 `CadViewport`、`CadRenderOptions`。
- 定义 `CadRenderInvalidation`、`CadScreenRect` 和多 dirty rect 局部刷新模型。
- 定义 `ID3D11ImageSource` 桥接接口，支持 WPF 图像源按 dirty rect 刷新。

### Direct2dCad.Rendering.Transient

临时绘制预览场景模型层。

主要职责：

- 定义绘制模式中的临时图形，例如 circle / arc / ellipse / line / polyline / spline / polygon / rectangle / text 预览。
- 定义选择框、复制粘贴预览、snap marker、绘制辅助线和测量文字。
- 使用可递归变换的 transient group 表示跨文档 Block 粘贴预览，并纳入局部刷新、图像和 OLE 缓存管理。
- Transient 图形的 stroke、fill、hatch、line weight 应尽量与最终实体绘制一致。
- 不负责命令执行，也不把临时图形持久化到 `CadDocument`。

### Direct2dCad.Rendering.Handles

选中实体可视化 handle / grip 场景模型层。

主要职责：

- 定义选中外框、grip / handle 点、handle 场景。
- 提供 handle 场景构建和 handle 命中测试所需的数据模型。
- 描述 handle 的位置、类型、尺寸和显示方式。
- 不直接修改 `CadDocument`，实际移动或缩放由 Editor / Commands 完成。

### Direct2dCad.Rendering.Direct2D

Direct2D 渲染实现层，应该保留为独立项目。`Direct2dCad.Rendering` 是抽象；`Direct2dCad.Rendering.Direct2D` 是当前后端实现。

主要职责：

- 使用 Direct2D 绘制 `CadDocument`。
- 使用 DirectWrite 测量和绘制 TrueType 文本。
- 管理 Direct2D geometry / brush / text layout / hatch brush 等资源缓存。
- 绘制 background、grid、origin、实体、transient overlay、selection handle overlay。
- 支持 full render 和多 dirty rect 局部刷新。
- 处理 D3D11 / D3D9 shared surface 与 WPF `D3DImage` 交互。
- 在 `EndDraw` 出现可恢复设备失败时重建设备资源并触发全量重绘。

原则：绘制时不应该临时创建所有实体资源；实体创建、修改、删除时应通过 change tracking 驱动资源创建、更新和释放。特殊情况下可以延迟创建，但不能让正常绘制路径变成主要资源构造路径。

### Direct2dCad.IO

文件读写层。

主要职责：

- 保存和读取 `CadDocument`。
- 定义 `.d2cad` 文件容器和 section。
- 支持 section 级版本迁移。
- 支持读取单独 section，例如只读取 settings。
- 序列化文档级 view settings、layer、style、fill / hatch、origin、entity 等内容。

### Direct2dCad.Client.Common

客户端通用模型与用户设置层。

主要职责：

- 定义 `CadUserSettings`。
- 定义用户级渲染和交互偏好，例如选中颜色、选择框颜色、grip 颜色、是否开启抗锯齿等。
- 提供 enum description / localization 相关辅助。
- 明确区分用户偏好和图纸文档内容。

设置边界：

- `CadDocument` / `CadViewSettings` 保存与图纸相关的内容，例如背景、网格、原点、图层、绘制优先级。这些应该随 `.d2cad` 保存。
- `CadUserSettings` 保存与当前用户相关的偏好，例如选中颜色、选择框颜色、handle 颜色、抗锯齿开关。这些不应该写入图纸文件。

### Direct2dCad.Lang

多语言资源层。

主要职责：

- 管理 resx 语言资源。
- 提供 `LangKeys` 和 `Strings` 资源访问。
- 支持 WPF 中的 `I18N` XAML 绑定。
- 当前 UI 文本应优先通过 Lang 资源绑定，不应在 XAML 中散落硬编码文本。

### Direct2dCad.ViewModels.Abstractions

WPF / ViewModel 共享的轻量抽象层。

主要职责：

- 定义 `CadCanvasToolMode`。
- 定义画布输入结果、光标类型、鼠标按钮等输入 DTO。
- 定义 WPF/XAML 需要直接绑定的 ViewModel enum。
- 避免 WPF 项目为了绑定 enum 而依赖重型 ViewModel 服务实现。

### Direct2dCad.ViewModels.Services

非 UI 的 ViewModel 业务服务层。它用于把 `CadDocumentViewModel` 中的绘制、交互、几何、渲染协调等职责拆出来。

主要职责：

- Platform：定义 ViewModel 依赖的平台边界，按 Dialogs、Importing、Ole、Notifications、Settings、Toolboxes 分组；其中用户设置使用 Store 语义，工具箱图标使用 Provider 语义，主题和语言明确为 Application 级能力。
- Events：定义 MessagePipe 消息，例如 document interaction state、view settings、editor tab document summary、theme changed。
- Drawing：绘制状态、绘制点击处理、绘制实体创建、绘制预览、绘制默认样式。
- Geometry：绘制预览和 grip drag 相关几何构造。
- Interactions：pan、selection window、copy / paste、grip drag、viewport 初始化等交互控制器。
- Rendering：overlay scene 协调、render resource attach/detach、render invalidation 计算。
- Snapping：鼠标吸附逻辑。
- Styling：预览样式、layer-following 样式解析。
- Text：文本测量服务，隔离 DirectWrite 测量能力对 ViewModel 的影响。

### Direct2dCad.ViewModels

WPF ViewModel 层。

主要职责：

- 定义 `MainViewModel`、`EditorTabViewModel`、`CadDocumentViewModel`。
- 定义文档、图层、属性、搜索、选择过滤和命令行等 Toolbox ViewModel。
- 绑定绘制模式、选择状态、图层、实体属性、用户设置和文档设置。
- 协调 transient scene、handle scene 和 `Direct2DImageRenderHost`。
- 使用 `Direct2dCad.ViewModels.Services` 中定义的服务接口和 MessagePipe 消息。

`CadDocumentViewModel` 的方向：只保留画布输入协调、命令入口和状态聚合。绘制预览、grip drag、snapping、render invalidation、文本测量等细分逻辑应继续放到 `Direct2dCad.ViewModels.Services`。

### Direct2dCad.wpf.Controls

WPF 控件库项目，项目文件为 `Direct2dCad.wpf.Controls/Direct2dCad.wpf.Controls.csproj`。

主要职责：

- 放可复用 WPF 控件。
- 不依赖业务项目。

### Direct2dCad.wpf

WPF 应用层。

主要职责：

- 提供 WPF 启动入口、`MainWindow`、`CadCanvas`。
- 提供 Ribbon、StatusBar、文档、图层、属性、搜索、选择过滤和 Terminal 等 View。
- 实现 `Direct2dCad.ViewModels.Services/Platform` 中定义的平台能力，并在 `Services/Application`、`Dialogs`、`Importing`、`Ole`、`Notifications`、`Toolboxes` 中按职责组织。
- 承载 `D3D11ImageSource` / `D3DImage`。
- 通过依赖注入装配 ViewModel 和 WPF 服务。

## 画布交互规则

当前画布交互由 `CadCanvas` 转换 WPF 输入，再交给 `CadDocumentViewModel` 处理。

核心规则：

- 左键在 Select 模式下优先命中 grip / handle；命中后进入移动或缩放预览状态。
- grip / handle 拖动时，松开鼠标不提交、不退出状态，只释放鼠标捕获并继续显示预览。
- grip / handle 拖动状态下再次左键点击，才提交移动或缩放命令。
- 右键或中键用于 pan，不需要单独的 Pan 工具模式。
- `Esc` 在任何状态下都回到 `Select` 模式，并清理绘制状态、选择框、grip drag、paste preview 等临时交互状态。
- `Enter` 用于完成当前多点绘制，例如 polyline、polygon、spline 等。
- 鼠标滚轮执行 zoom，并保留选中 handle overlay 的正确显示。

当前绘制模式包括：

```text
Select
Line
Rectangle
CircleCenterRadius
CircleCenterDiameter
CircleTwoPoint
CircleThreePoint
ArcThreePoint
ArcStartCenterEnd
ArcStartCenterAngle
ArcStartCenterLength
ArcStartEndAngle
ArcStartEndDirection
ArcStartEndRadius
ArcCenterStartEnd
ArcCenterStartAngle
ArcCenterStartLength
ArcContinue
EllipseCenter
EllipseAxisEnd
EllipseArc
Polyline
Polygon
Spline
Text
SetOrigin
```

## 绘制和资源更新原则

- 实体创建、修改、删除后，命令结果应携带 `CadDocumentChangeSet`。
- Editor 根据 change set 更新选择、索引、bounds 和渲染资源。
- Direct2D 后端根据 change set 创建、更新或释放 geometry / brush / hatch / text layout 等资源。
- 绘制顺序同时考虑 layer drawing priority、实体 `ZIndex` 和实体加入顺序。
- 实体颜色、line weight 可以设置为跟随 layer；这种情况下实体自身属性仍可保存，但绘制时使用 layer 的最终外观。
- fill / hatch 的颜色应使用统一 fill color；hatch pattern 不应额外绘制不需要的背景色。
- Transient 绘制应尽量复用普通实体绘制规则，只把辅助线、测量文字、snap marker 作为额外 overlay。
- 局部刷新 dirty rect 需要考虑 geometry、line weight、fill / hatch、handle、transient preview 和旧位置/新位置的合并区域。

## 项目引用表

| 项目 | 当前项目引用 |
|---|---|
| `Direct2dCad.Db` | 无 |
| `Direct2dCad.ChangeTracking` | `Direct2dCad.Db` |
| `Direct2dCad.Commands` | `Direct2dCad.ChangeTracking`, `Direct2dCad.Db` |
| `Direct2dCad.CommandLine` | 无 |
| `Direct2dCad.Editor` | `Direct2dCad.ChangeTracking`, `Direct2dCad.Commands`, `Direct2dCad.Db`, `Direct2dCad.HitTesting`, `Direct2dCad.Indexing`, `Direct2dCad.Rendering` |
| `Direct2dCad.HitTesting` | `Direct2dCad.Db` |
| `Direct2dCad.Indexing` | `Direct2dCad.Db` |
| `Direct2dCad.IO` | `Direct2dCad.Db` |
| `Direct2dCad.Rendering` | `Direct2dCad.ChangeTracking`, `Direct2dCad.Db` |
| `Direct2dCad.Rendering.Transient` | `Direct2dCad.Db` |
| `Direct2dCad.Rendering.Handles` | `Direct2dCad.Db` |
| `Direct2dCad.Rendering.Direct2D` | `Direct2dCad.ChangeTracking`, `Direct2dCad.Db`, `Direct2dCad.Rendering`, `Direct2dCad.Rendering.Handles`, `Direct2dCad.Rendering.Transient` |
| `Direct2dCad.Client.Common` | `Direct2dCad.Db` |
| `Direct2dCad.Lang` | 无 |
| `Direct2dCad.ViewModels.Abstractions` | `Direct2dCad.Client.Common`, `Direct2dCad.Lang` |
| `Direct2dCad.ViewModels.Services` | `Direct2dCad.ChangeTracking`, `Direct2dCad.Client.Common`, `Direct2dCad.Db`, `Direct2dCad.Editor`, `Direct2dCad.Rendering`, `Direct2dCad.Rendering.Direct2D`, `Direct2dCad.Rendering.Handles`, `Direct2dCad.Rendering.Transient`, `Direct2dCad.ViewModels.Abstractions` |
| `Direct2dCad.ViewModels` | `Direct2dCad.CommandLine`, `Direct2dCad.ChangeTracking`, `Direct2dCad.Client.Common`, `Direct2dCad.Editor`, `Direct2dCad.IO`, `Direct2dCad.Lang`, `Direct2dCad.Rendering.Direct2D`, `Direct2dCad.Rendering.Handles`, `Direct2dCad.Rendering.Transient`, `Direct2dCad.ViewModels.Abstractions`, `Direct2dCad.ViewModels.Services` |
| `Direct2dCad.wpf.Controls` | 无 |
| `Direct2dCad.wpf` | `Direct2dCad.CommandLine`, `Direct2dCad.wpf.Controls`, `Direct2dCad.Editor`, `Direct2dCad.ViewModels`, `Direct2dCad.ViewModels.Services` |

## NuGet 依赖

| 项目 | NuGet 依赖 |
|---|---|
| `Direct2dCad.Db` | `StronglyTypedId` |
| `Direct2dCad.Editor` | `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `Direct2dCad.IO` | `MessagePack`, `Riok.Mapperly` |
| `Direct2dCad.Lang` | `Antelcat.I18N.SourceGenerators` |
| `Direct2dCad.Rendering.Direct2D` | `Vortice.Direct2D1`, `Vortice.Direct3D11`, `Vortice.Direct3D9` |
| `Direct2dCad.ViewModels.Services` | `CommunityToolkit.Mvvm` |
| `Direct2dCad.ViewModels` | `CommunityToolkit.Mvvm`, `Dirkster.AvalonDock.Core`, `Dirkster.AvalonDock.Mvvm`, `Dirkster.AvalonDock.Mvvm.CommunityToolkit`, `MessagePipe`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `Direct2dCad.wpf` | `Antelcat.I18N.WPF`, `CommunityToolkit.Mvvm`, `Dirkster.AvalonDock`, `Dirkster.AvalonDock.DependencyInjection`, `Dirkster.AvalonDock.Themes.Arc`, `gong-wpf-dragdrop`, `MahApps.Metro`, `MaterialDesignThemes.MahApps`, `MessagePipe`, `Microsoft.Extensions.DependencyInjection` |

## 构建

```powershell
dotnet build .\Direct2dCad.slnx
```

