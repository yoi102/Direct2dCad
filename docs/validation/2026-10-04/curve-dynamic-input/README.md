# 圆、圆弧和椭圆的动态参数输入验证

2026-10-04，Windows x64、.NET SDK 10.0.401，本地未提交工作区。操作说明见[绘图与编辑](../../../DRAWING-AND-EDITING.md#精确输入和捕捉)。本记录补充 [2026-10-03 动态输入验证](../../2026-10-03/dynamic-input/README.md)，各轮结果不累加。

## 输入行为

圆的半径/直径、圆弧的半径/扫角/弦长/方向角、椭圆的两个半轴，以及椭圆弧的半轴和起始角/扫角，现在提供可编辑的短数值框，沿对应几何辅助线布局。左侧使用 `R:`、`D:`、`A:`、`L:`、`X:`、`Y:` 等前缀，角度右侧显示 `°`。椭圆的 `X:`、`Y:` 表示半轴长度；普通点坐标仍在光标右下角。

鼠标点击字段或按 `Tab` / `Shift+Tab` 进入编辑，直接键入数字也可进入第一个字段。输入立即更新预览，`Enter` 或点击字段以外的画布确认。已编辑的值固定，未编辑值继续跟随鼠标；`Ctrl+Delete` 释放固定值，`Esc` 取消。显示使用文档精度和单位，未编辑字段确认仍使用完整几何精度。输入框高 24 DIP，宽度按内容在 56–128 DIP 间调整，并避让相邻字段和画布边缘。

三点圆/圆弧必须保留已确认点，半径不得小于已确认弦长的一半；三点弧必须经过中间点。弦长不得超过直径，扫角必须处于 `(0, 360°)`。不相容输入保留步骤并显示简短错误。椭圆预览使用构造状态副本，确认后才应用，因此释放参数、取消、撤销和重做不会被预览改写。

## 验证结果

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| Release 应用与 UI 测试项目构建 | 通过，0 警告、0 错误 | `dotnet build Direct2dCad.UiAutomation.Tests/Direct2dCad.UiAutomation.Tests.csproj -c Release --no-restore -v quiet`，项目引用同时构建应用 |
| 模型专项 | **85/85**，0 失败、0 跳过 | `TestResults/curve-dynamic-input/model-verified/yoiri_YOIRI_2026-10-04_00_09_09_net10.0.trx` |
| 最终真实窗口专项 | **6/6**，0 失败、0 跳过，同一轮运行 | `TestResults/curve-dynamic-input/ui-final-accepted/yoiri_YOIRI_2026-10-04_00_27_13_net10.0.trx` |
| 新增三项窗口测试的 WPF 绑定日志 | 全部为空，0 绑定错误 | 最终截图目录内 `arc.bindings.log`、`circle-three-point.bindings.log`、`ellipse-arc.bindings.log` |
| 截图核对 | 已核对前缀、角度后缀、数值、几何位置和窄框 | 下方截图 |

模型专项包含 `DynamicInputTests`、`CurveDynamicInputTests`、`DrawingWorkflowContractTests`，覆盖所有圆弧构造方式、两点/三点圆、椭圆与椭圆弧、几何约束、单位、完整精度、预览、取消及历史。该次模型检查后，几何逻辑未变；后续调整限于字段无障碍名称、WPF 模板/焦点交互和 UI 测试。

最终窗口专项验证：三点圆点击 `R:` 输入 125 后在画布确认；圆弧半径 150、鼠标输入扫角 135°；椭圆弧 Tab 输入半轴 120/80 和扫角 150°，保持起始角 30°，字段不重叠且可鼠标聚焦，并检查提交结果和撤销重做。另回归原有圆动态输入、矩形对应边输入、样条同行输入。测试使用独立临时设置和恢复目录。

复现命令、每项窗口测试结果、TRX/源码/截图 SHA256 和绑定日志大小见 [verification.json](verification.json)。TRX 和日志保留在被 Git 忽略的 `TestResults/curve-dynamic-input` 中，截图副本保存在本目录。

## 最终截图

- [三点圆：R: 125](circle-three-point-radius-input.png)
- [圆：D: 直径输入](dynamic-input-diameter.png)
- [圆弧：A: 135°](arc-sweep-input.png)
- [椭圆弧：X: 120、Y: 80、A: 150°](ellipse-arc-parameter-inputs.png)

新增曲线的三张截图通过 Terminal 启动工具，因此上方保留 File Ribbon；这里不验证 Ribbon 自动切换。截图也不代表混合多屏 DPI 或完整人工制图验收。

## 修复和探索记录

早期三点圆鼠标点击未保留全选，已修正为首次点击选择整个数值。模板更改后，一轮 UI 检测把模板内前缀 TextBlock 当作独立 UIA 节点，改为检测实际字段的无障碍名称前缀，并人工核对截图；未删除手动输入、几何提交和位置断言。

`ui-accepted` 为 5/6，圆弧失败。后续 `arc-diagnostic` 和 `arc-events` 复现输入 `135` 变为 `135188.45`。事件追踪确认点击时已全选，但排队的预览刷新随后替换 Text、清除选区；现首次键入前再次全选仍在跟随鼠标的字段。已固定且有焦点的字段仍允许定位光标。独立 `arc-focus-fixed` 1/1 和最终 `ui-final-accepted` 6/6 通过，失败记录保留。

模型探索还发现退出连续圆弧时，渲染在字段刷新前访问旧步骤发生越界。现增加输入类型切换保护，相关模型流程通过。

## 验证边界

本次未执行完整产品回归、混合多屏 DPI、输入法或输入延迟测量。此前 GitHub 0.0.0.2 发布包未更新，本次工作区的构建和专项结果不代表新的公开发布或签名。
