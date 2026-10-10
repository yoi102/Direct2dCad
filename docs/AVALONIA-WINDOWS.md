# Windows Avalonia / NativeAOT 交付记录

日期：2026-10-11。Windows x64 Avalonia 客户端复用现有 WPF 的 CAD 文档、编辑器、命令和业务模型。UI 布局、工具分组、原图标、快捷键和操作路径尽量相近；不是像素级复刻。自动验证、真实桌面操作与未验证环境分别记录。

本轮 UI 的逐项修改和仍存在的差异见 [AVALONIA-UI-PARITY.md](AVALONIA-UI-PARITY.md)。按用户最新要求保持大致布局和等效操作，允许所有 UI 库保留相近的原生外观。自动检查、离屏截图和 Windows 桌面检查绑定哈希；下文 Office/AI/PDF 的早期桌面结果保留原候选归属。0.1.6 分发包的 EXE SHA256、本次 NativeAOT 自检与 GPU 共享帧结果见验证 JSON。UI 桌面操作记录仍绑定前一候选 `979AF9698509A9F0220E00F5942E936FC30B7DD957C2BCADCB41DF2FF856F240`；未声称这些人工操作已在本次 EXE 上重测。

## 实现

- Avalonia 12.0.5、Material.Avalonia / DataGrid 3.19.0、DialogHost.Avalonia 0.12.3；官方 ColorPicker 后备和 DataGrid 12.0.1；.NET 10、net10.0-windows、win-x64、PublishAot=true。部署程序不引用 WPF，无需安装 .NET Runtime。
- AvaloniaUseCompiledBindingsByDefault=true、类型化 x:DataType、静态视图分派。开发时读取 WPF 表单和原图标，生成 47 种 ViewModel 视图，无待审查项。MessagePack / JSON 使用生成元数据，AI 异构 DTO 保留明确元数据，MessagePipe 使用显式闭合 broker。
- 共用绘图、曲线编辑、标注、布尔运算、属性、撤销/重做、图层、块、布局/视口、文件/DXF、恢复和 AI 引擎。包含多文档、搜索/筛选/消息、命令行、图片、剪贴板和文件拖放。
- Ribbon 保留 WPF 的大小按钮组、下拉层次与矢量图标；工具选中状态绑定实际模式。画布右键菜单有 43 个命令；底栏有网格类型、主/副间距、网格吸附、对象捕捉、正交和极轴。支持文档进度与取消、只读提示、块编辑退出、语言、明暗主题。
- Dock.Avalonia / Dock.Model.Mvvm / Dock.Avalonia.Themes.Fluent 12.0.0.2：默认左侧文档/图层页签、右侧通高属性、底部命令行；保留左上/左下、右上/右下、底左/底右目的区域，空区域自动收起，支持原生停靠、自动隐藏、浮动/关闭，关闭工具通过视图菜单或快捷键重开。生成 JSON 元数据保存完整原生布局、分割比例、活动页、自动隐藏的原归属和浮动窗口大小位置；重启创建真实 HostWindow。
- 属性表单保留 WPF 标题、嵌套折叠分组、默认展开状态和内容插槽顺序；数字编辑保留 F3/F2 等格式、范围与步长，显示格式不回写几何值。“随图层”线宽有只读显示，颜色来源控制显式颜色编辑。侧边/底边按钮可隐藏和恢复原工具箱，属性数字框支持文档 Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y。
- Ribbon 主图标为 50px，并使用适配明暗主题的可读强调色；图层卡片包含颜色、名称、可见/锁定/冻结、优先级、实体数和线宽，块卡片保留 ID/E/R 信息，命令行输出保留时间戳。
- 动态输入支持 Tab / Shift+Tab、Enter、Escape、Ctrl+Delete、F4；离开画布保留预览。Canvas 支持选择循环、R、撤销/重做、复制/剪切/粘贴及原工具箱快捷键。中键轮盘保持 Shift > Ctrl > Alt 优先级，并在松键时更新。
- Direct2D 通过 keyed mutex D3D11 BGRA 共享纹理与 Avalonia compositor 互操作；最多三个槽位，处理尺寸变化、设备丢失和背压。Direct2D 为顶端原点，导入明确指定 TopLeftOrigin=true。不支持互操作与 Headless 时提供 CPU 位图 fallback。
- Windows 打印记录 ID2D1CommandList，用 ID2D1PrintControl 提交矢量页。预览内联真实打印机/纸张/方向/份数/DPI，支持视图/图纸范围、实际/适应/自定义比例、驱动设置、纸张与可打印边界；完整 DEVMODE 经驱动验证，DPI 传入 PrintControl；图片和 OLE 保留位图内容。
- NativeAOT OLE 和矢量打印使用 C# 与 Microsoft.Windows.CsWin32 0.3.333；接口和回调 ABI 来自包，ComWrappers 管理 COM 身份与引用计数。沿用 WPF D2CAD-OLE1 封装，保留 Office 剪贴板嵌入、绘制、双击编辑和通知回写。自写 C++ 源码、构建步骤及 Direct2dCad.Ole.Native.dll 已移除；当前实机验收范围见最新验证记录。
- Avalonia 设置目录独立；首次读取 WPF 通用设置，不改 WPF 设置文件。验证使用独立 settings/recovery 目录。

## 自动验证

最终 EXE 哈希、时间、具体检查项和 GPU 测量在 [avalonia-native-verification.json](avalonia-native-verification.json)。发布脚本校验报告与 EXE SHA256 相同；每次使用新目录。Headless 和 offscreen GPU 不创建应用桌面窗口、不操作鼠标键盘。

- Release 构建：0 警告、0 错误。locked restore 通过，共享项目锁文件未改变。
- NativeAOT 动态代码关闭；实际硬件 Direct2D 帧、101 个 AI schema、48 次绘图属性视图实例化通过。
- 真实 DataGrid 编辑/提交、排序、NaN 拦截、只读索引、撤销与文化数字转换通过；关闭自动列生成，使用编译列绑定。
- 压缩 d2cad、生成标注 JSON、DXF、恢复/设置往返，静态 OLE 保存/加载/绘制，4 MiB 历史预算通过。
- 路由鼠标、离开画布、Tab 数字输入、Enter/Escape、撤销/重做、轮盘修饰键松开、完整右键绑定、六区停靠、浮动/回停靠、明暗 Skia 全窗渲染通过。
- Direct2D / XPS 检查实际矢量路径和文字 glyph；打印纸背景正确且恢复文档背景。
- compositor 实际导入共享 GPU 帧，GPU 呈现路径 CPU readback 为零。另有标识清楚的 8 帧 CPU readback benchmark。100,000 实体首帧及提交/完成耗时见最终报告，不代表显示器 FPS 或物理输入延迟。

此前共享回归（本轮 UI 修改未重新运行整个共享矩阵）：AI 32、Agent 25、Agent.Codex 27、Application 60、ViewModels.Services 114、Commands 113、ViewModels 1138、IO 72，共 **1581 通过 / 0 失败**。后续 Direct2DRenderHostIntegration / EllipticalRegionRendering / OrientedSelectionPixel 后端检查 **161 通过 / 0 失败**。共享逻辑及离屏测试不替代全桌面 UI 矩阵。

## 实际 Windows 操作

通过 Windows Computer Use 操作自行创建的测试实例和测试文件。下列 Office/AI/PDF 属于之前候选的检查；当前 Dock 和打印预览操作、截图与未运行项记录在 avalonia-ui-parity-verification.json，不把早期操作自动归属于新 EXE。

- 圆工具、点击圆心、Tab 聚焦半径、输入 12、Enter 提交、Escape、撤销/重做、原生保存/打开。
- 安装的 Excel：A1:B2 粘贴成 application/x-ole-storage 对象；画布双击打开 Worksheet in CAD object - Excel；B2 从 24 改为 36，Update/关闭后 CAD 收到 Update OLE Object，保存 ui-ole-saveback-36-20261009.d2cad。
- 真实 LM Studio：UI 设置连接 http://127.0.0.1:15630/v1 并取得 3 个模型，选择已加载 prism-ml/bonsai-27b；模型调用 CAD 建线 (0,0)→(80,30)，随后查询到 1 个实体，保存 ui-ai-bonsai-line-20261009.d2cad。本地模型没有被替换成模拟 provider。
- Microsoft Print to PDF：实际/适应比例改变预览，真实驱动生成 ui-vector-print-20261009.pdf；pypdf 检查 1 页、8 个曲线操作、零 Image XObject，Poppler 渲染后人工检查圆形正常。
- 实机发现共享纹理上下翻转，已明确设置顶端原点修复；最终包以非对称 OLE 文字和鼠标命中位置复验。离屏成功本身不足以证明方向正确。
- 早期候选的明暗主题实机文字、图标、面板可读；当时以同一圆图纸对照 WPF 和 Avalonia 实际截图、控件树及源布局，未声称逐像素复刻。

这些文件保存在 .artifacts/avalonia-native-validation/，具体二进制归属见 JSON。

## 验证边界

每个绘图工具、所有属性路径、多种 DPI/IME、多窗口长时间负载、物理打印机及 Codex provider 尚未逐项实机覆盖。Office 和 PDF 结果适用于本机实际安装的服务/驱动。100k 测量是离屏宿主/合成提交耗时，没有 WPF 物理显示帧率、输入延迟或显存对比。

依赖仍有展开的裁剪/AOT 警告：MessagePack 动态 fallback、MessagePipe 诊断扫描、SharpGen、DataGrid TypeHelper/默认转换器和反射字段/枚举元数据。应用采用生成 resolver、编译绑定、专用数字转换器和定向元数据保留；已测业务链路通过，不代表所有依赖 fallback 路径均可用。没有全局屏蔽诊断，完整日志在 .artifacts/avalonia-native-publish.log。

## 0.1.6 GitHub 安装包

Avalonia 版的 NativeAOT Windows x64 MSI 与便携 ZIP 已加入 GitHub 的 0.1.6 Release。MSI 带安装目录选择页、应用图标、开始菜单和桌面快捷方式；便携包解压后可直接运行。两种包均不要求单独安装 .NET Runtime。安装包未签名，也未发布到 Microsoft Store。

## 使用

解压 .artifacts/Direct2dCad-Avalonia-win-x64.zip 后运行 Direct2dCad.Avalonia.exe，保持原生 DLL 与 EXE 同目录。每组模块使用独立哈希目录，保留仍在运行的旧版本。

重建：`./Direct2dCad.Avalonia/publish-windows.ps1 -HeadlessValidation`。生成：`dotnet run --project tools/Direct2dCad.Avalonia.Generate -p:UseSharedCompilation=false -- .`。托管构建只需 .NET 10 SDK；NativeAOT 发布仍需 Windows SDK 和 Visual Studio Build Tools 提供的 MSVC 原生链接器，属于 .NET NativeAOT 工具链要求。项目没有自写 C++ 编译步骤。

## 此前基线包与精确二进制证据（E8FAB61D）

可运行目录：`.artifacts/Direct2dCad-Avalonia-win-x64-abdac3b28c12/`。EXE SHA256：`E8FAB61DE02E747CAB8E7B20341B52124B88B22107F87A1DDDD07727579E39C8`。自测运行 ID：`67648255c2a3431eb7b0af2989b6a0e5`。

该 EXE 的 **25 项 NativeAOT 自测全部通过**；GPU 实际导入 20 帧，呈现路径 CPU 回读 0。100,000 实体首帧 34.7859 ms；GPU 提交平均 0.11005 ms、完成平均 6.991025 ms；另行 CPU readback benchmark 平均 0.8607375 ms（8 帧）。以上是离屏宿主耗时。

最终 EXE 实机复验：OLE 字体正向，重新加载值 36，点击实际文字位置选中，属性显示 126.7 / 40.58739297 / 37.5 且选中不产生几何修改。宽度改 100 后撤销恢复原尺寸。真实模型成功查询 1 个 OleObject；浮动窗口标题为中文“图层”，回停靠通过。圆心 (-56,39)、半径 18 的 Tab 数字绘图、Enter/Escape、Ctrl+Z / Ctrl+Shift+Z、原生保存通过，文件为 `ui-circle-18-final-e8fab61d-20261009.d2cad`。

最终 PDF：`ui-vector-print-final-e8fab61d-20261009.pdf`。真实 Microsoft Print to PDF 输出 1 页，8 个曲线操作，零 Image XObject；Poppler 渲染检查通过。PDF SHA256：`93CE8E9FC781959FC8F076DCD67D9C9EDD52C87D1B7437BFC5EC4CCC4A04DD0A`。

Office 实际编辑回写和模型建线发生在较早的 `19014D40...` 候选；最终程序重新载入其结果并完成上述查询和交互复验。JSON 分开记录各项二进制归属。数字格式化修复使用独立 TextBox 派生控件，格式化不回写 Value；新增自测检查精确 Bounds 未改变、有效编辑、NaN 拦截和精确撤销。最后 Release 构建 0 警告/0 错误、locked restore 通过。


## 历史 UI 对齐交付包（6B996C0C）

该历史目录：`.artifacts/Direct2dCad-Avalonia-win-x64-4e87d0432000`。EXE SHA256：`6B996C0CCB7CD53D4D0A2998A46C07D11E95859CA856CC61E9DAB95C4FA46D94`。

本轮补齐属性标题/折叠组/插槽顺序、数字步进/范围/格式、随图层线宽显示、颜色来源禁用逻辑、面板边缘入口、图层优先级/实体数、块 ID、命令行时间戳、Ribbon 主图标尺寸及明暗可读强调色。数字属性焦点外移时的 Ctrl+Z 问题已修复。

本轮 Release 构建 0 警告/0 错误，locked restore 通过，生成视图待审查项 0。当前 NativeAOT EXE 的 27 项检查全部通过，含聚焦数字框的路由 Ctrl+Z、整数值类型/上下界、折叠默认状态、信息分组、随图层只读值和颜色编辑状态。GPU compositor 实际导入 20 帧，GPU 路径 CPU readback 为零。

当前 EXE 桌面复验：同一圆图纸与 WPF 实际界面对照；半径 18→步进 19→Ctrl+Z 恢复 18→Ctrl+Shift+Z 重做 19→Ctrl+Z 恢复；侧栏隐藏/恢复保留 Circle1 及半径；高级组依次显示 ID、可见、绘制顺序、测量、信息/直径 36；外观组显示随图层的只读线宽 0.250。明暗主题和图层颜色预览均正常。原合成图纸未被覆盖。Office 编辑、真实模型调用及 PDF 输出仍按早期二进制哈希记录；当前 EXE 复验范围见 JSON，不将历史证据冒充本轮实测。

此前记录与当前结果分别保留；未验证环境、依赖裁剪/AOT 警告、签名/公开发布边界保持上述说明。

## 最新 UI 库版本

本轮 Material、DialogHost 和 Dock.Avalonia 接入、紧凑布局、模态行为、原生停靠、打印选项和绘图徽标改动见 [UI 库审查](AVALONIA-UI-LIBRARIES.md)。最新二进制及验证以 avalonia-native-verification.json 为准；历史桌面观察按原哈希保留。

工具栏文件下拉已改为原生菜单，支持 Down/Enter/Escape；属性 Enter 验证后返回画布，不连带完成绘图，无效数值拦截保存/另存为/打印快捷键。Escape 先由控件处理。文件和面板菜单显示快捷键，样条图标的 Viewbox 测量已修正。新增验证使用相同 NativeAOT EXE，具体检查数和桌面范围见最新 verification JSON。


## 2026-10-10 停靠、画布与闪退修复

- 实际复现右键平移后的原生上下文菜单误开，菜单接管后续滚轮。右键按下即捕获，拖动开始时抑制指针菜单；松开释放捕获。输入回归要求真实平移已开始、结束无菜单、立即滚轮缩放有效，随后普通右键菜单仍可打开。
- 默认导航工具合并为左上页签，属性占左侧较大区域，底部集中命令/消息。空停靠槽位由原生布局隐藏，不占固定空白。文件菜单的“视图 → 重置 · 布局”可恢复默认分组并关闭浮动窗口，保留业务视图与活动图纸。
- Windows 事件日志确认旧 73F1 候选曾发生 InvalidateArrange on wrong LayoutManager；跨窗口视图转移改在布局完成后分阶段执行，先脱离并清空旧布局队列，再挂入新宿主。补充六轮失效布局的浮动/回贴靠和布局重置回归。
- 事件日志确认打印 NumericUpDown 的 decimal 无障碍通知导致 UnsupportedType 闪退。使用原 NumericUpDown 主题和行为，专用 peer 的范围值及变化通知转换为 double；保留 Windows 无障碍功能，并验证其操作到达驱动份数设置。
- 属性面板补齐标注重新关联/解除关联按钮，保留标注公共实体属性，只有无活动图纸时才显示创建/打开提示。只读文档禁用实体编辑，表单不因无关通知反复重建。
- 未处理异常现在记录到 %LOCALAPPDATA%/Direct2dCad/Avalonia/logs；隔离验证使用自己的 settings/logs。日志保留异常，未通过全局忽略异常掩盖闪退。

本次最终验证和桌面证据按新 EXE SHA256 记录在两个 verification JSON。早期 73F1、AB70 的观察及崩溃均保留原候选归属。
- 补充修复标题栏 Material 菜单 48px 项被 32px 标题栏裁切的问题，继承库主题设置紧凑高度，并用真实指针点击验证打开。恢复停靠的文档宿主回调使用世代检查，旧回调不再切换当前图纸/脱离画布；Windows 窗口验证包含恢复布局后的实际 Direct2D 帧。
- 工具区域至少 240×140，画布至少 320px；侧栏横向边缘贴靠归入已有原生页签组，避免生成挤窄的新列。上下分组、浮动、自动隐藏和六区域移动继续保留。
- 整体侧栏边缘贴靠同样归入目标工具页签；回归同时检查所有可见工具区域及画布边界。用户已接受 Dock 与 WPF 不完全一致，最终交付采用这个较稳定的布局规则。
- 整组标题拖动额外通知布局变化，以 Normal 优先级合并更新尺寸和全部侧栏归属；延迟视图转移同样在当前调用栈完成后及时执行。



## 2026-10-10 VS 风格停靠操作

- 修正“预览分割、松手却合并标签”的不一致：移除工厂改写，局部引导的中心合并标签，四方向执行原生分割。禁用整个工作区的外围停靠按钮，预览只覆盖当前目标面板。
- 保留 Dock 原生模板、命中测试和预览生命周期，只把白色位图引导换成 VS 蓝色矢量图。实际目标尺寸决定可见方向：工具最小 240×140，CAD 区域最小 320×240；中央合并将工具加入原生文档标签，保留不可关闭的 CAD 宿主，随时可切回图纸。
- 默认左侧文档/图层标签、右侧通高属性、底部命令行；其他工具从视图菜单或快捷键打开。删除重复的应用侧边图标栏，仅保留 Dock 原生自动隐藏标签。
- 布局版本升至 2；旧布局先备份到 dock-native.pre-vs.json，再采用新默认。版本 2 的用户布局仍恢复。“重置 · 布局”恢复固定默认并关闭浮动窗口，保持原图纸和业务视图。
- 此规则替代上文早期“横向贴靠强制合并标签”规则。最终二进制的自动与桌面验证范围，以两份 verification JSON 的当前 SHA256 为准。

- 浮动菜单的 Dock 现在可用并返回原面板；浮动整个标题组时仍保留主窗口目的区域。布局保存/恢复也保存浮动工具的返回位置，回停靠最后一个工具时关闭空浮动窗。
- 实机补充发现并修复自动隐藏预览固定按钮的递归风险，以及浮动返回留下空窗的问题。浮动身份以真实 Root.Windows 内容为准；稳定区域保护只作用于主窗口，浮动窗复制的 zone ID 不阻止关闭。新增原生预览 Pin 命令和未 Capture 的浮动返回回归。

本轮最终候选：`.artifacts/Direct2dCad-Avalonia-win-x64-ac6ebe566b23`，EXE SHA256 `BF7727F5AEF8EB13E8DA28153F19562A6ACA018847021FF69FBD0E2096739D90`。Release 编译 0 警告/0 错误；NativeAOT 57 项、Win32 38 项通过；GPU 20 帧，呈现 CPU 回读 0。相同哈希的桌面鼠标验证覆盖分割、合并、标签排序、自动隐藏/固定、浮动 Dock 返回及空窗关闭、菜单复位和活动图纸保留。物理跨屏拖动、DPI/IME 全矩阵未验证；NativeAOT 依赖分析警告仍保留。中间候选的失败及修复记录单独保留在 verification JSON，不作为当前通过证据。

## 2026-10-10 顶部标题栏与系统操作

主窗口把文件、置顶、明暗主题、语言和窗口按钮合并到一行 32px 标题栏，消除额外菜单行和顶部留空。使用 Avalonia 12 的 WindowDrawnDecorations 生命周期和非客户区角色；最大化按钮返回 Windows HTMAXBUTTON，支持系统贴靠布局。主窗口按钮由 Avalonia 绘制，设置、预览和浮动窗口使用完整 Windows 原生标题栏。工具面板仍拖内部标题或标签进行停靠，外层系统标题拖动浮动窗口。

最终 NativeAOT 包：`.artifacts/Direct2dCad-Avalonia-win-x64-32da2b2cd83c`，EXE SHA256 `4C2EFBFB21D6261633379B37393F86A3B8AD7C1D71A35451882B1413CCD7BDC4`。Release 0 警告/0 错误；NativeAOT 57 项、Win32 38 项通过；GPU 20 帧、呈现 CPU 回读 0。当前哈希的实机检查覆盖最大化悬停贴靠布局、最大化/还原、标题拖动、明暗切换、文件菜单/Escape、设置系统关闭、浮动窗口原生顶部和回停靠空窗关闭。截图和未运行范围见当前 verification JSON；此前完整 Dock、Office、打印桌面结果仍绑定历史哈希。依赖裁剪/AOT 警告继续保留。

## 2026-10-10 右键菜单和径向菜单修复

左上角使用 WPF 原应用图标，视图位于文件旁边。图纸标签和工具栏下拉支持右键菜单。文档管理、图纸恢复支持整行边缘和列表空白右键；点击行先选择该文档，列表空白使用当前选中项，无选中项时禁用单文档命令。

停靠菜单仅由面板标题、工具标签和标题箭头触发，内容区不再弹停靠操作。图层内容区提供新增、删除、显示、锁定、冻结、上下移动和刷新，沿用现有命令及撤销路径。画布在完成右键单击并释放捕获后直接打开 43 项编译绑定的原编辑菜单；右键拖动仍平移，结束后仍可滚轮缩放。径向菜单改为主窗口内的透明矢量浮层，尺寸 248px，取消独立弹窗的白色矩形背景，保留中键释放执行、修饰键切换及翻页。

当前 NativeAOT 包：`.artifacts/Direct2dCad-Avalonia-win-x64-bbc627e301dd`，EXE SHA256 `5587D72924C54E7AD7F4A21F770C9BD043C05A584639E859FD2A34AADFC782CF`。Release 0 警告/0 错误；NativeAOT 自动回归 65 项、Win32 运行回归 38 项通过；GPU 20 帧、呈现 CPU 回读 0。当前二进制的原生 PE、报告和 ZIP 哈希一致。截图来自当前哈希的自动渲染，未对当前包重放实机鼠标操作、系统菜单/Snap Layouts或完整 DPI/IME 矩阵；旧桌面验收仍绑定历史哈希。依赖裁剪/AOT 分析警告保留。

## 2026-10-10 画布菜单可见性和弹出位置修复

用户实机反馈确认此前菜单仍不可见。事件日志定位到生成的 ContextMenu 派生类没有沿用基类主题，原生弹窗实际为 0×0；同时 Win32 的默认上下文识别器会提前将右键释放标记为 handled。现已修复主题键和已处理事件接收，生成器同步输出主题键。新增回归检查模板、实际宽高和可见菜单项，取代仅以 IsOpen 判断菜单成功显示。

用户随后确认菜单显示，但原菜单高 852 DIP，居中的 anchor/gravity 又使弹窗偏离点击点。现改为标准 Pointer 定位、32 DIP 行高和 12 DIP 字体，按主窗口可用高度及目标屏幕工作区限制到最多 560 DIP，超长内容使用 UI 库原模板滚动。回归包含点击位置与菜单屏幕坐标检查、画布右下角重复打开、handled 释放后的 capture 清理；右键拖动画布和后续滚轮缩放回归保留。

当前 NativeAOT 包：`.artifacts/Direct2dCad-Avalonia-win-x64-37edfefccb5e`，EXE SHA256 `5A7C4DA43EE2AFFF6593F243CDE6919B768779D3D9C5F2D28E19909500312B93`。NativeAOT 66 项、Win32 38 项通过；GPU 20 帧，呈现 CPU 回读 0。当前包截图来自该哈希的自动渲染；当前原生包已由用户实机确认画布中间和右下角的菜单位置正常、超长菜单能够滚动。Win32 PopupRoot 日志记录 7 次原生打开测量，附于包内 canvas-desktop-input.log；本次桌面验收仅覆盖画布菜单。依赖裁剪/AOT 分析警告保留；多屏 DPI/IME 全矩阵未运行。

## 2026-10-11 滚动条和属性面板紧凑化

修复菜单和打印窗口的滚动条在点击或悬停后变成宽灰块的问题。原库 ScrollViewer 的厚度样式使用的部件名称与模板不符，全局 RepeatButton 主题也会覆盖滚动条内部按钮。现将重复按钮主题限定于属性数值微调器，滚动条使用 BasedOn 继承 UI 库 MaterialModernScrollBar，命中区域固定 12 DIP，滑块保持 4–8 DIP；保留库的滚动、捕获和自动隐藏逻辑，没有自造控件模板。

25 种属性/临时属性表单对照当前 WPF 的布局收紧：编辑行约 28 DIP，减少行间距，Advanced/Appearance 等分组为透明背景、细分隔线、12 DIP 标题，移除厚卡片阴影；保留 WPF 的默认展开状态。数字微调按钮合并为 18 DIP 宽的上下箭头列。生成器与实际表单同步，编译绑定、图层选择、数值精度、验证、键盘编辑和撤销路径保留。

当前 NativeAOT 包：`.artifacts/Direct2dCad-Avalonia-win-x64-75dfbf159620`，EXE SHA256 `1D4D3851E967D9A19E3B1ED293E6A82818D61778ACC3B2A620AA502922F1C49C`。NativeAOT 69 项、Win32 38 项通过；GPU 20 帧，呈现 CPU 回读 0。新增实际菜单和打印 ScrollViewer 的悬停延迟、按下拖动、宽度及滚动偏移检查，并检查属性标题文字未被裁切。当前包图片来自该哈希的自动渲染；前一包的用户菜单位置确认保留为历史证据，用户已在当前 NativeAOT 窗口中确认菜单、打印预览的滚动条在悬停/按住时仍细窄，选中对象后的属性面板更简洁，本次反馈为“这几处都正常”。依赖裁剪/AOT 分析警告保留，多屏 DPI/IME 全矩阵未运行。

## 2026-10-11 下拉控件和菜单密度

全应用普通下拉框采用现有 UI 库的 outline 主题，透明背景、细边框、12 DIP 字体，字段约 28 DIP，状态栏约 24 DIP。颜色选择器保留库模板、颜色编辑弹窗与键盘/焦点行为，将实心紫色按钮换成中性细边框字段，显示所选颜色的色块和文字，并横向铺满可用空间；属性、状态详情、设置和打印等位置统一使用。使用静态转换器与 TemplateBinding，应用编译绑定保留。

文件、图层和其他命令菜单统一为 32 DIP 行高、12 DIP 字体，分隔线为 1 DIP、上下各 4 DIP，减少原有 48 DIP 项目和过大留白。新增实际 File/Layer 行高、图层菜单标题未裁切检查，保留图层右键选行、锁定与撤销回归。颜色选择器明暗主题预览、真实弹窗可见尺寸、Escape、普通列表 Alt+Down/Down/Enter 和禁用行为均通过。

当前 NativeAOT 包：`.artifacts/Direct2dCad-Avalonia-win-x64-d8c0c6771c10`，EXE SHA256 `BA7D7323EE6F5581531ADC024A8750D1440C3EF396B203C7FE77CA9E9DEADBF0`。Release 编译 0 警告/0 错误；NativeAOT 70 项、Win32 38 项通过；GPU 20 帧，呈现 CPU 回读 0。图片来自当前哈希的自动渲染；本轮颜色和菜单密度的实机外观尚待用户反馈，此前滚动条/属性面板反馈保留为历史哈希证据。NativeAOT 依赖裁剪/AOT 分析警告继续保留，多屏 DPI/IME 全矩阵未运行。

## 2026-10-11 Explicit 颜色来源和中文显示

用户确认上一包的菜单和下拉样式大小合适，同时报告 Color source 无法选中 Explicit。真实停靠属性面板的鼠标回归复现选中后又回到 ByLayer：命令触发属性刷新，重建选项集合使选择控件同步回写旧值。现保留不变选项的对象身份，且将集合变更通知也放在刷新保护期间，避免刷新产生额外业务修改。鼠标和键盘选择 Explicit、颜色字段启用、编译绑定颜色编辑以及分别撤销颜色和来源均通过。

中文资源文字未损坏；Windows 字体管理明确采用 Microsoft YaHei UI，并配置 YaHei UI、Yu Gothic UI 字体回退。回归检查截图中缺字的视图、语言、图层、删除/复制/剪切选中实体等实际排版字形没有 missing glyph，并保存中文菜单截图。保存提示的 Discard 改用已有 DontSave（不保存）资源，按钮结果仍为 Discard；补齐网格间距、布局、正在加载、覆盖和窗口置顶的中英日资源。切换语言同步刷新状态工具名，模型空间标签和 Paper/Model 按钮翻译只作用于展示，不修改用户图纸/布局名称。

当前 NativeAOT 包：`.artifacts/Direct2dCad-Avalonia-win-x64-8746a41a03f8`，EXE SHA256 `56A0D4E1B125F3C5068AAA4EE38CAC66B76551E3AD0BE0FDE8D2D2621E2CDEDD`。Release 编译 0 警告/0 错误；NativeAOT 72 项、Win32 38 项、属性刷新测试 13 项通过；GPU 20 帧，呈现 CPU 回读 0。图片来自当前哈希的自动渲染；用户已在当前中文版 NativeAOT 窗口实机确认 Explicit 可以选择并修改颜色，右键菜单中文显示和未保存图纸的“不保存”按钮正常，反馈为“这几处都正常”。上一包的外观接受和随后发现的问题均保留为历史哈希证据。NativeAOT 依赖裁剪/AOT 分析警告继续保留，多屏 DPI/IME 全矩阵未运行。

## 2026-10-11 NuGet / C# Windows interop

按用户要求，OLE 和矢量打印改用 Microsoft.Windows.CsWin32 0.3.333 的生成接口与纯 C# 实现。新增 Direct2dCad.Windows.Interop 项目，禁止运行时封送，使用 ComWrappers 管理 COM 身份和引用；没有自写 C++ 源码、编译脚本或 Direct2dCad.Ole.Native.dll。NativeAOT 和编译绑定保持启用；NativeAOT 本身仍需要 Windows SDK/MSVC 链接器。

当前包：`.artifacts/Direct2dCad-Avalonia-win-x64-15c721f8a6ce`；EXE SHA256 `3A8AF1B86FC0B6DF08EF63FF38E17C6B71E8DBCFB67F4B2DC9D97A296A6F59BE`。Release 0 警告/0 错误，managed 75 项、NativeAOT 76 项、Win32 41 项通过。OLE 静态对象的实际存储/加载/绘制、CAD 保存重载后的封装字节一致性、原生 COM 回调共享身份/通知/异常/释放后防护均通过。Direct2D XPS 保留矢量路径与文字 glyph；真实已安装驱动的 Unicode DEVMODE 转为 PrintTicket XML 并保留份数，未提交打印任务。GPU 导入 20 帧、呈现 CPU 回读 0，ZIP 与目录校验通过。

旧 Office 编辑回写和真实 PDF 打印的实机证据保留原哈希归属，不作为本轮 C# 迁移实机验收。当前包的真实 Office 剪贴板嵌入、双击编辑回写及物理/PDF 打印尚未重新人工验证。新截图来自当前哈希的自动渲染，NativeAOT 依赖裁剪/AOT 分析警告保留在发布日志中；多屏 DPI/IME 全矩阵未运行。

## 2026-10-11 退出确认和无边框浮动 Dock

WPF 主窗口先调用 ShowExitConfirmation，再检查未保存图纸；Avalonia 此前只检查未保存内容，因此空白或无修改时直接退出。现补齐同样顺序：关闭主窗口始终先确认退出，再处理保存、不保存或取消。连续关闭复用一个待处理任务；取消/Escape 保留程序并允许重试；退出流程异常显示通知并保留窗口。

按用户截图要求，浮动 Dock 小窗口删除外层 Windows 系统边框和标题栏，仅保留 Dock 库自己的标题、最大化/还原、关闭和回停靠功能。通过 ToolChromeControlsWholeWindow 让标题控制整个浮动窗，4 DIP 客户区边缘保留边/角缩放手柄及方向光标。单工具、多页签组和保存布局恢复均使用无边框配置；主窗口、设置和打印预览仍采用各自原有标题栏。

当前包：`.artifacts/Direct2dCad-Avalonia-win-x64-53b8bf9d90ee`；EXE SHA256 `979AF9698509A9F0220E00F5942E936FC30B7DD957C2BCADCB41DF2FF856F240`。Release 0 警告/0 错误，managed 82 项、NativeAOT 83 项、Win32 48 项通过。关闭空白窗口、重复关闭、取消/重试、确认后的未保存列表、取消保留几何、不保存不改磁盘、保存圆后实际关闭主窗口均通过。浮动标题最大化/还原、边缘光标、关闭只隐藏工具并去除空宿主、重开和原有回停靠/恢复回归通过。GPU 导入 20 帧、呈现 CPU 回读 0，ZIP 校验通过。

新增截图 exit-confirmation.png、exit-unsaved.png、dock-floating-borderless.png 来自当前哈希自动渲染。当前包的物理拖动、边角缩放、跨屏回停靠和退出按钮人工反馈待确认；旧实机反馈保留历史哈希。CsWin32/C# OLE 和矢量打印迁移保持，真实 Office 编辑及提交打印任务未重新实机验证，NativeAOT 依赖分析警告仍保留于发布日志。
