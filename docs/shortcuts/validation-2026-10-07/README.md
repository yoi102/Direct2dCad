# 2026-10-07 快捷键验证

[快捷键与焦点规则](../../SHORTCUTS-AND-FOCUS.md)

验收范围是应用/画布/Terminal/动态数值/AI 输入框的快捷键分派、完整修饰键匹配、属性提交、模态保护与浮动 CAD 窗口。测试使用本地 Release 构建；工作区基线为 `8766f612efb780c2df777ae38b3ef3b4b3a93a59`，实际验证包含未提交的本轮改动，精确源码和程序集 SHA-256 见 [verification.json](verification.json)。

## 结果

| 验证 | 结果 | 证据 |
| --- | --- | --- |
| 项目引用架构 | 41 项目、123 引用，无环或禁止依赖 | regression.txt |
| 架构检查器 | 11 个独立图样例通过 | regression.txt |
| 覆盖汇总器 | 固定样例回归通过；没有采集新的业务覆盖率 | regression.txt |
| 完整解决方案 Release 构建 | 0 警告、0 错误 | regression.txt |
| 托管回归，13 个项目 | 1943 通过、0 失败、0 跳过 | regression/ 内逐项目 TRX |
| Windows 集成回归 | 351 通过、0 失败、1 跳过 | regression/Direct2dCad.Windows.IntegrationTests.trx |
| 快捷键控件回归 | 39 通过、0 失败、0 跳过，属于上述 351 项，不重复计入总数 | focused/shortcut-routing.trx |
| 真实窗口快捷键回归 | 9 通过、0 失败、0 跳过 | ui/shortcut-ui.trx |
| 本地化资源 | 英文、中文、日文各 4 个范围提示键，无空值 | verification.json |

Windows 跳过项是 `SuppliedDxfLongStrokesPreserveFullPathRasterCoverageAcrossZoomAndPan`：运行未提供 `DIRECT2DCAD_UI_DXF_SAMPLE` 外部图纸样例。它与快捷键整改无关，不作为快捷键通过项。

## 实际断言

控件回归使用真实实体属性控件的双向绑定：未失焦的名称在保存/另存为/打印前提交，错误角度保持原输入并阻止命令；禁用命令和模态状态阻止有效属性的提交。测试检查真实 AvalonDock 浮动文档/工具箱模型的归属及路由器释放，普通 Owner 对话框不继承应用快捷键。

画布测试创建真实实体并操作图纸历史，验证 Ctrl+Shift+Z 重做、额外 Alt/Shift 修饰键不删除或结束绘图；Terminal 测试检查草稿、建议、绘图会话、Tab 补全与分阶段 Esc。捕捉测试在真实对象捕捉控制器上确认 F4 改变当前候选，Ctrl+Tab 保持候选不变。

桌面测试通过 FlaUI 与原生键盘事件启动独立进程，使用临时设置和恢复目录，覆盖：

1. Terminal 焦点下 Ctrl+N 恰好新建一张图纸，草稿保留。
2. Terminal 焦点下 Ctrl+O 与 Ctrl+Shift+S 打开相应原生文件窗口；窗口内 Ctrl+N 不增加图纸，Esc 关闭后草稿保留（2 项）。
3. Ctrl+J 与旧 Ctrl+Oem3 各切换显示/自动隐藏一次；无建议 Tab 与 Shift+Tab 可以离开输入框，重开后草稿保留。
4. 画布 Ctrl+Z 撤销实体、Ctrl+Shift+Z 恢复；Terminal 内 Ctrl+A/Delete/Z 编辑和恢复文字，图纸实体保持不变。
5. Terminal 和画布 Ctrl+Tab 打开实际文档导航器，释放 Ctrl 后切到 Welcome，草稿保留。
6. AI 输入框 Shift+Enter 换行，Ctrl+N 新建一张图纸且保留原提示词。
7. 从实际文档菜单执行 Float，在浮动原生内容宿主取得键盘焦点后，Ctrl+S 打开保存对话框并可以取消。
8. 既有 Terminal 焦点 Ctrl+S 保存用例通过。

旧 OEM 别名随键盘布局变化；自动化仅向测试进程发送英文布局切换并在退出测试时恢复，不修改全局输入法设置。旧别名测试使用直接虚拟键投递，Ctrl+J 不依赖 OEM 键帽。

## 测试中发现的问题

- 最初新建图纸退出：自定义 I18N 扩展不能直接放在 WPF `Setter.Value`。已改为本地化文本资源的 Binding；最终真实窗口用例加载该菜单通过。
- 初版文档计数依据 TabItem.Name，但框架暴露模型类型名。改为读取实际 `DocumentExplorerItem`，继续断言恰好增加一张图纸。
- Ctrl+Tab 导航器需等待异步模板加载与选中项获得焦点，再释放 Ctrl。用例等待真实 `PART_DocumentListBox` 的焦点，保留最终文档切换断言。
- 浮动内容跨独立 HWND，当前 UIA 树未提供画布节点，但截图确认画布正常呈现。用例通过实际文档菜单浮动、枚举同进程窗口并验证其子 HWND 的原生键盘焦点，再发送 Ctrl+S；控件回归另验证模型归属。没有把 UIA 节点缺失当作画布缺失或放弃浮动保存断言。

## 复现命令

在仓库根目录串行执行：

```powershell
.\scripts\testing\Run-Regression.ps1 -IncludeWindowsIntegration -ResultsDirectory TestResults/shortcut-2026-10-07/regression

dotnet test Direct2dCad.Windows.IntegrationTests/Direct2dCad.Windows.IntegrationTests.csproj -c Release --no-build --filter FullyQualifiedName~ShortcutRoutingTests --logger "trx;LogFileName=shortcut-routing.trx" --results-directory TestResults/shortcut-2026-10-07/focused

$env:DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY = Join-Path $PWD "TestResults/shortcut-2026-10-07/screenshots"
dotnet test Direct2dCad.UiAutomation.Tests/Direct2dCad.UiAutomation.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~MainWindowUiTests.Shortcut|FullyQualifiedName~SaveShortcutWorksWhileTerminalInputRetainsKeyboardFocus" --logger "trx;LogFileName=shortcut-ui.trx" --results-directory TestResults/shortcut-2026-10-07/ui
```

## 限制

此次不运行全部其他桌面自动化，也未进行人工中文/日文输入法、读屏、多屏 DPI 验收。浮动工具箱的模型归属已测；真实窗口保存验证覆盖浮动文档，未穷举每一个浮动工具箱。AI 换行和文件快捷键已测，没有通过真实 provider 发送提示词或改写系统剪贴板；实际打印机输出不在此次验收中。

测试不重启或关闭用户原窗口。已有进程需保存图纸后运行新构建才能加载改动。未提交、推送、打包或发布。

## 截图

![Terminal 草稿保留并新建图纸](screenshots/shortcut-new-from-terminal.png)

![AI 换行草稿在新建图纸后保留](screenshots/shortcut-ai-new-and-newline.png)

![实际浮动图纸的画布](screenshots/shortcut-floating-document.png)
