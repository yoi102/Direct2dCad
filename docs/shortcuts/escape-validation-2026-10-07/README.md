# 2026-10-07 应用范围 Esc 验证

[快捷键与焦点规则](../../SHORTCUTS-AND-FOCUS.md)

本轮补齐主窗口及所属浮动 CAD 窗口的 Esc。验收包含未提交的本地修改，基线为 `8766f612efb780c2df777ae38b3ef3b4b3a93a59`；源码、程序集与证据哈希见 [verification.json](verification.json)。首轮快捷键归档保持历史快照，此目录记录 Esc 补充后的构建。

## 最终结果

| 验证 | 结果 | 证据 |
| --- | --- | --- |
| 架构与检查器 | 41 项目、123 引用，无环/禁止依赖；11 个隔离样例通过 | regression.txt |
| Release 解决方案构建 / 最终 UI 增量构建 | 0 警告、0 错误 | regression.txt、ui-build.txt |
| 托管回归（13 项目） | 1944 通过、0 失败、0 跳过 | regression/ 各项目 TRX |
| Windows 集成 | 363 通过、0 失败、1 跳过 | regression/Direct2dCad.Windows.IntegrationTests.trx |
| 快捷键控件测试 | 51 通过，已包含在上述 363 项中 | 同上 |
| 真实窗口回归 | 17 通过、0 失败、0 跳过；含既有快捷键与动态数值场景 | ui/shortcuts-ui.trx |
| 本地化 | 中/英/日各 4 个更新的 Esc 提示均非空 | verification.json |

Windows 的跳过项为外部 DXF 长线样例：未提供 `DIRECT2DCAD_UI_DXF_SAMPLE`，不计作通过。快捷键控件测试包含在 Windows 汇总内，不重复累计。

## 验收内容

- 应用路由在未处理的 `KeyDown` 冒泡阶段兜底；已被子控件处理、附加修饰键、IME 处理键和模态状态不会取消图纸。按键重复不会穿透下一取消层。
- 面板按钮、Terminal 和 AI 输入框的 Esc 取消当前绘图，恢复画布焦点；文字草稿保留，无活动图纸时保留输入。
- Terminal 第一次 Esc 关闭建议，第二次取消绘图；正在执行的命令通过原取消源终止，保留下一条命令草稿并丢弃迟到成功结果。显式输入 `CANCEL` 的原语义保留。
- 实体名称/旋转框恢复尚未提交的文本，验证错误清除，选择保持；回到画布后再次 Esc 清除选择。回读属性和文档历史验证取消未引入修改。已提交的实时属性编辑继续使用文档撤销。
- 实际下拉框第一次 Esc 只关闭下拉框，第二次才清除图纸选择。实际文件模态窗口关闭时保持原绘图模式。
- 实际动态直径输入的非法数值在第一次 Esc 后复位，焦点回画布而绘图模式保留；第二次取消操作，已有实体数量不变，原精确圆和撤销/重做断言保留。
- 实际文档通过 Float 菜单浮动。文件保存快捷键仍有效；从主窗口 Terminal 按 Esc 后，绘图取消，原生前台窗口回到浮动文档，并确认键盘焦点位于该窗口的子内容宿主。

## 发现并修复的问题

浮动文档使用独立子 HWND，其画布不在浮动 Window 的普通可视树内。现在除窗口可视树外，还按活动文档身份查找 WPF `PresentationSource`，校验原生根窗口属于当前应用路由后恢复焦点。最终测试同时检查取消状态、前台窗口和子宿主键盘焦点。

完整 Windows 集合中的其他控件测试创建了不同 Dispatcher 的 Application，最初遍历其 Windows 集合产生跨线程异常。现在在访问集合及窗口前检查线程归属，只操作当前 UI 线程对象；原失败断言保持，完整集合重新执行。

桌面组合测试发现 UIA 点击点位于控件的物理边界之外，例如按钮边界 `(1182,1625,50,50)`，返回点击点却为 `(804,1579)`，实际鼠标也落在该错误位置，导致按 Esc 前尚未进入绘图模式。测试辅助方法现在等待可见且启用的控件，验证可点击点位于其边界内，否则使用边界中心进行真实鼠标点击。工具激活、Esc、文档切换和动态数值等断言均保留；没有改变系统显示设置。实际多屏 DPI 的人工产品验收仍单列为未执行。

该桌面集合还将自己创建的测试窗口固定在主显示器的可用区域，避免启动位置随当前鼠标所在屏幕变化。文档准备步骤通过实际 TabItem 的选择模式切换图纸，不再误取图纸列表中同名文字。最终组合运行使用该固定显示条件；这不代表副屏或多种 DPI 的产品验收已完成。

文件快捷键组合测试曾出现一次打开窗口等待超时。输入准备现在同时确认原生前台窗口和输入框键盘焦点；打开/另存为专项复核 2 项通过，随后按同一最终程序集重新执行完整相关窗口组合。

## 复现

在仓库根目录串行执行：

```powershell
./scripts/testing/Run-Regression.ps1 -IncludeWindowsIntegration -ResultsDirectory TestResults/escape-regression

$env:DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY = Join-Path $PWD 'TestResults/escape-ui/screenshots'
dotnet test Direct2dCad.UiAutomation.Tests/Direct2dCad.UiAutomation.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~MainWindowUiTests.Shortcut|FullyQualifiedName~SaveShortcutWorksWhileTerminalInputRetainsKeyboardFocus|FullyQualifiedName~CanvasDynamicInputSupportsTabTypingPreviewEdgesAndExactCircleUndo' --logger 'trx;LogFileName=shortcuts-ui.trx' --results-directory TestResults/escape-ui
```

## 边界

桌面测试启动隔离进程并使用临时设置目录，不依赖、关闭或重启用户原窗口。没有执行其他全部桌面用例，未做人工中文/日文 IME、读屏、多屏 DPI 验收。IME 已测到 WPF 键事件边界，不以此替代真实输入法验收。浮动工具箱归属有模型回归，实际跨原生窗口焦点验证覆盖浮动文档，未穷举所有浮动工具箱。

未实际打印、发送真实 provider 请求或改写系统剪贴板；未提交、推送、打包或发布。已有应用需保存图纸后重新运行新构建才能加载改动。

## 截图

![Terminal Esc 后保留草稿](screenshots/escape-terminal.png)

![AI Esc 后保留草稿](screenshots/escape-ai.png)

![图层按钮 Esc 后回画布](screenshots/escape-layers.png)

![浮动文档](screenshots/shortcut-floating-document.png)
