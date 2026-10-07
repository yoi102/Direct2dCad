# Enter 上下文确认验证

[快捷键规则](../../SHORTCUTS-AND-FOCUS.md) · [Esc 历史快照](../escape-validation-2026-10-07/README.md)

本轮将 Enter 补为主窗口和所属 AvalonDock 浮动窗口中的上下文确认入口。绑定在未处理的 KeyDown 冒泡阶段；窗口 PreviewKeyDown 不截获 Enter。实体属性、Terminal、动态数值、AI 以及其他编辑控件各自保持输入语义。

## 验收行为

- 被动面板确认活动绘图并回到该图纸画布；浮动画布通过对应 HWND 的 WPF presentation source 定位。
- 单行实体属性先提交绑定，错误时保留文本及焦点；成功回画布但不继续完成绘图。多行属性仍换行，数值调节器与下拉框按本地规则处理。
- Terminal 有选中建议时接受补全，否则提交；AI 不可发送时 Enter / Ctrl+Enter 保留草稿并消费按键，Shift+Enter 换行。
- 动态坐标错误不会落到绘图确认；纠正后只提交当前点。点数不足时继续保留绘图步骤。
- 输入法及死键事件不重解释为 Enter；未知或具有输入职责的控件不触发图纸兜底。独立对话框和嵌入模态状态不被抢键。
- 模拟长按重复事件不跨步骤提交；按钮和多行文本的原生行为保留。

## 验证入口

```powershell
dotnet build Direct2dCad.slnx -c Release --nologo -v quiet
scripts/testing/Test-Architecture.ps1
scripts/testing/Test-ArchitectureGuard.ps1
dotnet test Direct2dCad.Windows.IntegrationTests -c Release --no-build --logger trx
dotnet test Direct2dCad.ViewModels.Tests -c Release --no-build --logger trx
dotnet test Direct2dCad.UiAutomation.Tests -c Release --no-build --filter FullyQualifiedName~MainWindowUiTests.Shortcut --logger trx
dotnet test Direct2dCad.UiAutomation.Tests -c Release --no-build --filter FullyQualifiedName~ApplicationLaunchesWithAccessibleMainSurface --logger trx
```

UI 自动化使用独立进程及临时设置、恢复目录。真实 UI 用例串行发送按键，验证前台窗口与焦点；Windows 控件测试使用 WPF 路由事件，验证控件的实际键盘焦点和所属线程的原生焦点，不要求占有桌面的全局前台窗口。

## 回归发现与修复

真实浮动输入记录还发现产品路由问题：同一次 Enter 先以 `FloatingWindowContentHost / IsRepeat=false` 到达，再以 `CadCanvas / IsRepeat=true` 转发到子 HWND。直接检查 IsRepeat 会丢弃首次确认。新增 `CadEnterKeyGuard` 配对未处理的代理事件与实际焦点、来源和修饰键，仅消费一次；松开键、关闭路由器或配对失效后清理。主窗口与浮动画布的单次 Enter 均已加入实际窗口回归，另有原生重复 KeyDown 序列验证。

初轮浮动画布测试把窗口中心当作空白画布，但截图显示该位置正好是长度 L 输入框，因此单次 Enter 正确执行了数值提交。测试现点击远离该字段的画布位置，并在原焦点等待绘图提示回到首点，再查询实体数量；仍要求一次 Enter 创建实体、一次撤销移除实体。浮动内容的 UIA 仅暴露 HwndWrapper 宿主，测试结合原生焦点归属与实际图纸结果验证，不把不存在的子 AutomationId 当成产品故障。

完整 Windows 回归另暴露旧状态栏测试调用 Application.Shutdown 的进程级影响：后续测试无法创建可见窗口。状态栏和快捷键测试改用共享后台 STA Application；每个用例清理新增窗口，临时资源在用例结束时还原。属性验证和焦点断言保持。

原生重复按键回归还发现停靠生命周期缺陷：隐藏并重开 Terminal 时，`ActiveDockContent` 经由 `CommandLine → null → CommandLine` 切换，原代码把临时 null 当作非 CAD 文档并解除工具箱关联，造成 `STATUS` 返回 `No active document.`。现保留临时空激活之前的图纸上下文，欢迎页和关闭图纸仍明确清理。测试直接重开 Terminal 后检查实体数、撤销和撤销后的实体数，不通过手动重新激活图纸绕过缺陷。

一次整组 UI 回归在新建菜单准备阶段超时，未进入 Enter 断言。该准备方法此前只检查按钮的 WPF/UIA 焦点，未验证桌面前台窗口。现发送 Down 前显式激活测试窗口并同时核对原生前台 HWND 和按钮焦点；仍只发送一次按键，并保留菜单出现断言。最终重新执行整组快捷键回归，重试结果不累加。

另一轮的全部 Enter 用例通过，但 Ctrl+Tab 回归读取弹出导航器时，单次 UIA 查询抛出 COM 超时。`WaitForElement` 现与已有 `WaitUntil` 一样在原等待期限内重新读取，并把持续失败的原异常附在超时结果中；不重发按键、不跳过导航结果断言。

后续整组运行中，AI 输入框的 Ctrl+N 用例未观察到第二张图纸。该用例原先只在控件上调用 `Focus()`，现发送按键前确认主窗口是桌面前台、AI 输入框拥有实际键盘焦点，并确认起始只有一张图纸；单独回归通过。仍要求一次 Ctrl+N 恰好创建一张图纸、原草稿不变，失败时保存截图。

## 证据与边界

最终 Release 构建为 0 警告、0 错误；架构检查为 41 项目、123 项目引用，11 项隔离保护样例通过。ViewModels 测试 1126/1126 通过，Windows 集成 393 通过、1 跳过，快捷键真实窗口回归 27/27 通过，欢迎页切换 1/1 通过。跳过项需要用户提供外部 DXF 样例。单独通过的针对性测试用于定因，不与整组计数累加。

最终计数、源码和程序集 SHA-256、测试报告与截图哈希记录在 [verification.json](verification.json)。测试结果不与首轮、Esc 或调试重试次数累加。此前快照保留其原始哈希，不代表本轮源码。用于定因的临时产品输入日志代码已移除。

本轮验证聚焦 WPF 输入和停靠上下文改动，重跑 ViewModels 项目全部测试、Windows 集成、快捷键及欢迎页 UI 回归，未重跑其他托管项目或其他 UI 流程。输入法候选窗口、读屏、多屏 DPI、物理键盘长按的人工验收及真实 AI provider 发送未执行。Windows 外部 DXF 样例门槛以测试报告的跳过原因单独记录。

原用户进程没有被重启；保存图纸并运行新构建后才能使用更新。
