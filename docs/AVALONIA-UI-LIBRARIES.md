# Avalonia UI 源码审查与库选择

2026-10-09。目标是 Windows、NativeAOT、应用编译绑定；按用户最新要求保持 WPF 的大致布局和操作，允许所有 UI 库保留相近的原生控件样式，包括 Dock.Avalonia 的外观。

## 源码结论与落地修改

WPF `App.xaml` 组合 MaterialDesign3、MahApps 和 ArcDark；主窗口使用 DialogHost。Avalonia 原先采用 Fluent 控件主题和独立确认窗口，单改尺寸无法消除模板差异。

- `App.axaml` 接入 Material 原生 ControlTheme，按钮使用 flat variants，保留 ripple/焦点/禁用状态。样式调整模板内部的输入高度和页签边距，不重新实现控件模板。
- Material 默认输入区域最小高度 56px，默认页签使用 UniformGrid。改为 28px 输入区域、18px 信息数字框以及靠左按内容宽度排列的页签；大按钮的英文标题换行，避免截断。
- Material 替换了原 Fluent 模板部件名。列表选择边框、文档选择边框和页签指示线改为库实际存在的部件。正文、背景、分隔线转用 Material 动态资源；深色背景沿用 WPF 的 #212121，避免 Material 默认黑色背景扩大视觉差异。
- `ThemeService` 同步持久化主色/辅色到 CustomMaterialTheme，BaseTheme=Inherit 跟随明暗切换。Fluent 作为官方 ColorPicker 和未被 Material 提供的控件后备；应用业务绑定与新增模板属性绑定均为编译绑定。
- `MainWindow` 和设置窗口接入 DialogHost。消息、保存冲突、未保存确认、块/AI/网格表单、进度使用窗口内覆盖层；用户/文档设置继续使用独立设置窗口，窗口内部支持嵌套网格弹窗。
- 浮层打开时禁用底层内容，并隔离 CAD 快捷键与绘图 Enter。输入校验失败保持打开；Tab 在浮层内部循环；Escape 取消；关闭后恢复焦点。
- 替换保存/冲突弹窗的空结果显式映射到 Cancel，避免枚举默认值变成 Save/Overwrite。进度 IDisposable 绑定自己的会话，旧进度结束不会关闭新弹窗。

## 候选库

以下版本由 NuGet 包内 nuspec 和对应 tag 源码核对。Avalonia 保持 12.0.5；没有为 UI 库升级整个框架。

| 库 / 版本 | 选择 | 原因及边界 |
| --- | --- | --- |
| [Material.Avalonia 3.19.0](https://www.nuget.org/packages/Material.Avalonia/3.19.0) | 已接入 | MIT；Avalonia >=12.0.0；最接近现有 WPF Material 风格。3.20/3.21 要求 12.1.1，未采用。 |
| [Material.Avalonia.DataGrid 3.19.0](https://www.nuget.org/packages/Material.Avalonia.DataGrid/3.19.0) | 已接入 | 为现有官方 DataGrid 提供 Material 样式；继续使用手写编译列和专用数字转换器。 |
| [DialogHost.Avalonia 0.12.3](https://www.nuget.org/packages/DialogHost.Avalonia/0.12.3) | 已接入 | MIT；Avalonia >=12.0.0；覆盖层加入主窗口视觉树，传入实际 Control，避免运行时视图反射。 |
| [Dock.Avalonia 12.0.0.2](https://www.nuget.org/packages/Dock.Avalonia/12.0.0.2) | 已接入 | MIT；配套 Dock.Model.Mvvm 和 Fluent 主题，同步既有六区业务模型；复用原工具视图，支持浮动、拖动、自动隐藏和关闭/恢复。布局用生成 JSON 元数据保存，不依赖反射 ViewLocator。 |
| [Semi.Avalonia 12.0.3](https://www.nuget.org/packages/Semi.Avalonia/12.0.3) | 未采用 | 可匹配框架版本，但外观离现有 WPF Material 更远。 |
| [FluentAvaloniaUI 3.1.0](https://www.nuget.org/packages/FluentAvaloniaUI/3.1.0) | 未采用 | 需要 Avalonia >=12.1.0，且 Fluent 外观无法解决此次主要差异。 |
| [Material3.Avalonia](https://github.com/greepar/Material3.Avalonia) | 未采用 | 0.2.0-preview.1 宣称支持 NativeAOT，但为预览并要求 12.1.1；没有实际接入或验证该库。 |

主要源码：[Material v3.19.0](https://github.com/AvaloniaCommunity/Material.Avalonia/tree/v3.19.0)、[DialogHost v0.12.3](https://github.com/AvaloniaUtils/DialogHost.Avalonia/tree/v0.12.3)。新增 UI 库及其 Roboto 字体授权文本附在 `THIRD-PARTY-UI-LICENSES.txt`。

## 验证与边界

最终 EXE SHA256、逐条 NativeAOT 检查、GPU 结果记录在 `avalonia-native-verification.json`；截图和未运行项在 `avalonia-ui-parity-verification.json`。检查包含真实 headless 输入路由、底层禁用、校验、取消/替换、Tab 循环、焦点恢复、进度会话生命周期以及属性数字精度和 DataGrid 编辑回归。截图是 NativeAOT 的 Skia 实际渲染，不是设计稿；它们没有证明实机 DPI/IME 或物理输入效果。

应用绑定编译不代表第三方库内部没有普通 Binding。Material 内部的可选 DataGrid/TreeDataGrid 自动发现产生 IL2072/IL2035；本应用显式引用 MaterialDataGridStyles，不使用 TreeDataGrid。共享 MessagePack、MessagePipe、SharpGen、DataGrid 的裁剪/AOT 警告也仍保留，没有全局屏蔽。通过实际已测路径后才接受这些库，不宣称所有依赖路径都 AOT 安全。

Dock 库主题本身使用编译绑定；应用通过强类型 FuncDataTemplate 提供原有视图，保留库所需的模型元数据。原生布局持久化保留分栏比例、活动页、隐藏项、自动隐藏的原归属和浮动窗口大小位置。打印预览已加入真实打印机/纸张/方向/份数/DPI 内联选项；驱动私有选项继续使用系统窗口。完整工具/DPI/IME 的实机矩阵见 `AVALONIA-UI-PARITY.md`。

工具箱保留自己的业务视图实例；模板每次建立轻量容器，避免 Dock 延迟宿主的逻辑父节点阻止跨窗口回挂。浮动窗口沿用主窗口的 CAD 快捷键路由，数字框中的文档撤销/重做有真实 headless 窗口输入检查。工具箱转为文档时显示原生文档导航，移回后恢复紧凑 CAD 文档栏，并保留原活动图纸。


停靠采用 Dock 原生局部方向引导，中心合并页签，四方向执行分割。去掉曾造成预览/执行不一致的工厂重写，禁用整个工作区外围引导，按真实目标尺寸隐藏放不下的分割方向。只替换引导 Image 的 Source 为 VS 蓝色 DrawingImage，保留原生模板和命中测试；无需新增 UI 包。

