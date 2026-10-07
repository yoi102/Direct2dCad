# 快捷键与输入焦点

[返回首页](../README.md) · [绘图与编辑](DRAWING-AND-EDITING.md) · [交互优化](INTERACTION-OPTIMIZATION.md)

2026-10-07：快捷键按应用窗口、画布、Terminal、动态输入和 AI 输入框分派。文件与工具箱命令在主窗口及所属浮动 CAD 窗口可用；文字编辑由获得焦点的输入控件处理。这里的“应用范围”只指 Direct2dCad 自身窗口，未注册操作系统全局热键。

## 审查结论与修复

原有入口分散：保存有主窗口绑定，部分编辑快捷键只在画布上处理；打开、新建、另存为缺少统一入口，浮动窗口不会沿主窗口事件树冒泡。画布以“包含 Ctrl”匹配撤销等操作，Ctrl+Shift+Z 会错误地撤销，附加 Alt 等按键也可能命中文档操作。Terminal 无条件处理 Tab，Shift+Tab 也会补全，导致键盘导航受阻。动态输入的 Ctrl+Tab 与 AvalonDock 文档导航冲突。

已用统一手势目录与窗口路由器修复上述问题，增加 Ctrl+Shift+Z 重做、Ctrl+J 打开/隐藏 Terminal、Ctrl+Shift+B 打开/隐藏块工具箱，保留 Terminal 原 Ctrl+Oem3 别名。捕捉候选切换改用 F4，Ctrl+Tab 由 AvalonDock 文档导航处理。菜单手势和本地化提示说明生效范围，画布保持紧凑数字框布局。

## 应用窗口

| 快捷键 | 操作 | 焦点与上下文 |
| --- | --- | --- |
| Ctrl+N | 新建空白图纸 | 主窗口、Terminal、AI 输入、属性框和浮动 CAD 面板 |
| Ctrl+O | 打开原生图纸 | 同上，按文件菜单现有打开命令执行 |
| Ctrl+S | 保存 | 当前活动图纸；无图纸时不执行 |
| Ctrl+Shift+S | 另存为 | 当前活动图纸；无图纸时不执行 |
| Ctrl+P | 打印 | 当前活动图纸；继续遵守现有打印命令的可执行条件 |
| Esc | 逐层取消并回到画布 | 主窗口及所属浮动 CAD 窗口；先由当前控件处理，剩余事件再取消活动图纸操作 |
| Enter | 按焦点确认 | 控件先处理；单行实体属性校验提交；被动面板可确认活动绘图并回画布 |
| Ctrl+Tab / Ctrl+Shift+Tab | 文档导航 | 停靠区交给 AvalonDock 导航器；不切换捕捉候选 |

保存、另存为和打印先更新当前实体属性文本编辑器的绑定。数值转换或验证失败时，不执行文件命令，保留输入及焦点，允许继续纠正。Terminal 草稿与尚未确认的动态数值不随文件快捷键提交。工具箱操作不提交这些输入。

单独的文件、设置、打印等模态窗口使用自身键盘操作。窗口路由器仅识别主窗口与实际 AvalonDock 浮动文档/工具箱，不因 `Owner` 关系接管普通对话框。主窗口内嵌 `DialogHost` 打开或正在确认退出时，应用快捷键被阻止。禁用的命令不执行，也不提交属性输入。

## Esc 的处理顺序

Esc 在应用内统一可用，只匹配未附加 Ctrl、Shift、Alt 的按键。每次按下处理一层，长按产生的重复事件不继续取消下一层。

1. 输入法组合输入、模态对话框、菜单、下拉框先使用自己的处理规则。已经处理的 Esc 不会继续取消图纸。径向菜单先关闭菜单。
2. Terminal 有补全建议时先关闭建议并保留草稿；没有建议但正在执行命令时，仅请求取消该命令，保留下一条命令草稿。输入 `CANCEL` 仍使用原来的显式命令语义。
3. 实体属性文字框恢复绑定源中的值、清除当前验证错误，然后回到画布，保留选择。已实时提交的属性修改不会被 Esc 撤销，可使用撤销命令。动态数值框放弃当前步骤的数值锁定和错误提示，回到画布，保留绘图步骤；再按 Esc 取消绘图。
4. 其余位置（含图层、工具箱按钮、Terminal、AI 输入框）取消活动图纸的绘图/编辑/粘贴预览，释放鼠标捕获并将焦点移回该图纸画布。没有活动操作时清除选择。Terminal 和 AI 草稿保持不变，AI 后台任务仍由专用停止按钮控制。

浮动图纸的画布可以从主窗口面板恢复焦点；所属浮动工具箱使用相同的窗口路由。无活动图纸时不清空输入、不关闭应用。Esc 的全局兜底在 `KeyDown` 冒泡阶段运行，文件快捷键仍在 `PreviewKeyDown` 处理。

## Enter 的处理顺序

Enter 在应用内统一分派，窗口兜底仅匹配裸 Enter，并在未处理的 `KeyDown` 冒泡阶段执行。主窗口及所属浮动 CAD 窗口共用规则，普通对话框保留默认按钮；输入法选字和死键组合事件交给输入控件。

1. 按钮、菜单、下拉框、列表、树、表格、搜索框、数值调节器等先执行自身行为。未处理的 Enter 也不会从这些控件穿透到图纸。多行属性文本保持原生换行。
2. Terminal 优先接受选中建议，否则提交命令；动态输入校验并提交当前步骤，错误时保留输入和焦点。AI 的 Enter / Ctrl+Enter 仅在允许发送时发送；发送禁用时消费按键并保留草稿，Shift+Enter 继续换行。
3. 直接绑定实体属性的可编辑单行 TextBox 校验并提交绑定，成功后回画布，当前按键不继续完成绘图。转换或验证失败时保留文本和焦点。内嵌下拉框、数值调节器仍由所属控件处理。
4. 画布以及明确无输入职责的面板表面，在存在活动绘图、编辑或预览时执行该操作现有的确认规则，并回到所属画布。点数不足保留步骤并提示；粘贴仍要求指定放置点。没有活动操作时不重复上一命令、不改变选择。未知控件不自动纳入兜底。

CAD、Terminal、动态输入、AI 和属性确认均抑制长按产生的重复 Enter；一次物理按下最多确认一步。独立的第二次按下仍可继续下一步。原生多行换行和按钮行为不改变。

AvalonDock 浮动内容使用独立子 HWND，同一次 Enter 会经过宿主代理再到实际控件，WPF 可能把首次转发标为重复。`CadEnterKeyGuard` 将未处理的首次代理与当前焦点及 presentation source 配对，只放行实际控件的一次确认；后续重复、焦点或修饰键变化均不复用配对，Enter 松开时清理。不能直接用 `IsRepeat` 丢弃浮动控件的首次确认。

## 工具箱

| 快捷键 | 工具箱 | ContentId |
| --- | --- | --- |
| Ctrl+J | Terminal | toolbox.command-line |
| Ctrl+Oem3 | Terminal，保留旧入口，键帽因键盘布局不同 | toolbox.command-line |
| Ctrl+Shift+E | 图纸列表 | toolbox.documents |
| Ctrl+Shift+L | 图层 | toolbox.layers |
| Ctrl+Shift+B | 块 | toolbox.blocks |
| Ctrl+Shift+G | 实体属性 | toolbox.entity-properties |
| Ctrl+Shift+D | 图纸恢复 | toolbox.drawing-assistant |
| Ctrl+Shift+T | 实体搜索 | toolbox.entity-search |
| Ctrl+Shift+F | 选择过滤 | toolbox.selection-filter |
| Ctrl+Shift+M | 消息 | toolbox.messages |
| Ctrl+Shift+A | AI 助手 | toolbox.ai-assistant |

主窗口和所属浮动 CAD 窗口共用框架的工具箱切换命令，保持停靠、自动隐藏与浮动状态的原有生命周期。停靠工具箱切换显示/自动隐藏；独立浮动工具箱按框架规则激活其窗口。一个键盘事件只执行一次。侧栏提示显示主手势，Terminal 以 Ctrl+J 为主。

工具箱隐藏、重开时，停靠框架的临时空激活不会清除当前图纸上下文，Terminal 和 AI 继续关联原图纸。切换到欢迎页等非 CAD 文档，或关闭当前图纸时，仍明确清理上下文。

## 画布与文字编辑

| 画布快捷键 | 操作 |
| --- | --- |
| Esc | 取消当前交互，清理临时指针手势 |
| Enter | 确认或完成当前绘图；点数不足保留当前步骤 |
| Backspace | 撤回上一未提交绘图点 |
| Delete | 删除选中的 CAD 实体 |
| Ctrl+A | 选择所有可选实体 |
| Ctrl+Z | 撤销图纸操作 |
| Ctrl+Y / Ctrl+Shift+Z | 重做图纸操作 |
| Ctrl+C / Ctrl+X / Ctrl+V | 复制、剪切、粘贴 CAD 对象 |
| Tab / Shift+Tab | 在重叠实体间循环选择；活动动态字段优先切换字段 |
| R | 曲线编辑期间重新选择编辑对象 |
| F4 | 有对象捕捉候选时切换下一个候选 |

输入框中的 Ctrl+A/C/X/V/Z、Delete、Backspace 使用控件原生文字编辑行为。画布命令不会从主窗口全局截获这些键。移除了未文档化的 Alt+A 全选别名，附加 Ctrl/Shift/Alt 的未分配组合不回退到另一个 CAD 操作。

## 动态数值、Terminal 与 AI

| 焦点位置 | 快捷键 | 行为 |
| --- | --- | --- |
| 动态数值 | Tab / Shift+Tab | 下一/上一字段 |
| 动态数值 | Enter | 校验并提交当前步骤，成功后回画布 |
| 动态数值 | Esc | 放弃当前步骤的数值锁定和错误提示并回画布；再次按下取消绘图 |
| 动态输入所属画布或字段 | Ctrl+Delete | 清除当前步骤的固定数值 |
| 动态输入所属画布或字段 | F4 | 存在候选时循环对象捕捉；不要求数字框当前可见 |
| Terminal | Enter | 已选建议先接受补全，否则提交；空输入继续现有完成/重复命令规则 |
| Terminal | Up / Down | 有建议时选择建议；无建议或正在回看历史时切换历史 |
| Terminal | Tab | 仅有建议时补全；无建议时正常移动焦点 |
| Terminal | Shift+Tab | 正常返回上一控件 |
| Terminal | Esc | 先关闭建议；否则取消正在执行的命令；否则取消绘图并回画布；保留草稿 |
| AI 输入框 | Enter / Ctrl+Enter | 发送，遵守 SendCommand 的可执行条件 |
| AI 输入框 | Shift+Enter | 换行 |
| AI 输入框 | Ctrl+V | 支持图片/文件时作为附件粘贴，否则交给文字粘贴 |
| AI 输入框 | Esc | 取消当前绘图并回画布；保留提示词草稿 |

Terminal 的 Ctrl+Enter、Ctrl+Up/Down、Ctrl+Tab 等附加修饰键组合不作为普通 Enter/箭头/Tab 处理。AI 输入只有完整匹配 Ctrl+V 时拦截附件，Ctrl+Shift+V 等组合不被当作附件粘贴。中文/日文输入法的组合输入事件保留给 WPF 控件；此次不声称已完成人工输入法验收。

## 代码职责与优先级

- `CadShortcutCatalog` 是手势目录，使用完整 Key/Modifiers 匹配，文件菜单显示字符串与执行入口同源。
- `CadWindowShortcutRouter` 在 Window 的 PreviewKeyDown 阶段处理文件/工具箱键，先检查模态状态与 CanExecute，再提交适用的属性并执行。Esc / Enter 使用未处理 KeyDown 冒泡入口，分别委托 `CadEscapeHandler` 和 `CadEnterHandler`；确认入口额外检查焦点类型及其祖先，不能仅凭事件未处理就完成图纸。处理后结束该事件，避免重复执行。
- 浮动窗口通过 `LayoutFloatingWindowControl.Model.Root.Manager` 找到所属主窗口，关闭主窗口后清除路由器的附加属性，无静态窗口列表。
- `CadDocumentView` 处理字段导航与捕捉；`CadCanvas` 处理 CAD 编辑并统一清理指针状态。Terminal 与动态输入的 Esc 在冒泡阶段处理，允许内部控件优先关闭弹出层；AI 的 Esc 交给窗口兜底。
- AvalonDock 5.0.0 的导航器在父级 `DockingManager.OnPreviewKeyDown` 处理 Ctrl+Tab。这是移除动态输入冲突手势的依据。

新增功能应先确定作用范围，并同步目录、提示及本文。避免新增主窗口的复制/撤销绑定绕过输入控件；组合键需要完整匹配。

## 验证与运行说明

Enter 补充验收见 [Enter 验证记录](shortcuts/enter-validation-2026-10-07/README.md)。[Esc 验证记录](shortcuts/escape-validation-2026-10-07/README.md)与[首轮验证记录](shortcuts/validation-2026-10-07/README.md)保留各自阶段的摘要和验证信息，不作为 Enter 改动后的哈希证明。桌面测试使用独立进程、临时设置及恢复目录，不依赖用户已有窗口。

首轮基线为 Release 构建 0 警告、0 错误，托管 1943 项、Windows 集成 351 项通过（含快捷键控件 39 项），真实窗口快捷键 9 项通过，1 项外部 DXF 样例跳过。后续 Esc 与 Enter 的计数分别记录在各阶段验证记录中；Enter 本轮未重跑全部托管测试，不沿用旧计数作为本轮通过证据。

已有运行中的应用不会热更新程序集。载入本次改动需保存图纸后自行运行新构建；测试不重启或关闭原用户进程。此轮不涉及提交、推送或发布。
