# Direct2dCad 属性面板布局与主题验收

属性面板统一采用现有 Material Design 与 MahApps 兼容控件，优先显示常用参数，将详细信息收在少量分组中。2026 年 10 月 8 日完成本轮调整，覆盖实体属性、绘制默认值、多选、块定义和标注参数。

## 视觉与交互

文本框和下拉框分别继承 `MaterialDesignOutlinedTextBox`、`MaterialDesignOutlinedComboBox`，数值框继承 `MaterialDesignOutlinedNumericUpDown`。复选框、按钮和折叠栏使用对应的 MD 基础样式。共享样式只调整字体、尺寸、间距和内容排列，保留库的控件模板、弹出窗口、鼠标与键盘状态。颜色选择器保留 MahApps 原模板，颜色内容使用小色块和颜色代码。

标签和值保持两列对齐，编辑器最小高度为 28 DIP。类型标题只显示一次；名称和图层在顶部，半径、宽高、半轴以及直线长度和角度位于坐标前。旋转保持直接可用。顶点表格利用面板宽度，减少重复滚动包装。

颜色、线宽、线型以及端帽和连接归入同一个“外观”区。派生直径、结束角、边界、矩形圆角、图片来源、实体 ID、绘制顺序和额外测量信息归入“高级设置”，减少连续的小折叠栏。填充和高级描边按需展开；多选的混合值提示、标注设置和所有原有编辑命令保留。

“随图层”的线宽显示为可选择和复制的只读值，继续禁止直接修改；启用自定义线宽后恢复数值编辑器。线宽与线型之间去掉重复的垂直留白。下拉框由 MD 模板处理点击、选项选择和 Esc 关闭，未另写 ToggleButton 或 Popup 模板。

## 明暗主题

标签、输入值和分组标题使用动态主题画刷；折叠栏显式跟随 MD 前景资源，避免基础样式的黑色回退值。标签不再叠加额外透明度。“随图层”的只读线宽避免数值控件和内部文本框同时禁用造成的双重变淡。

真实窗口截图：[深色中文面板](code-review/property-visual-2026-10-08/screenshots/panel-inspector-chinese-dark.png) · [浅色中文面板](code-review/property-visual-2026-10-08/screenshots/panel-inspector-chinese-light.png) · [深色圆与只读线宽](code-review/property-visual-2026-10-08/screenshots/panel-inspector-circle.png)。测试窗口临时关闭文档列表以展示较完整的属性内容；用户的停靠布局与设置保留。

## 验收

| 检查 | 结果 |
| --- | --- |
| Release 解决方案构建 | 0 警告、0 错误 |
| Windows 属性绑定、保存与快捷键 | 86 通过 |
| 真实窗口用例 | 13 个不同用例最终均通过 |
| 属性绑定保留比对 | 32 个视图和区块、368 条原绑定保留；应用资源另检 |
| MD 模板继承 | 共享控件样式使用库的基础样式，无自定义 ControlTemplate |

窗口用例包含圆、矩形、椭圆的主要参数可见性，增减数值与撤销，逐键输入 `55.5` 并重新选择后核对保存值，以及高级信息访问。明暗主题下分别验证中文标签、下拉框连续三次单击打开、Esc 关闭以及鼠标选择图层。还复验名称和非法旋转的 Enter/Esc、标注属性、面域和多选交互；最终截图对应的属性绑定日志为空。

首次窗口验收有一次设置对话框的 UI Automation 查询超时。同一构建的两种主题复验均通过，原始记录与复验记录一并保留，验收清单按每个用例的最新结果汇总。

补充检查中的[窄窗口矩形阵列首选预览用例](code-review/property-visual-2026-10-08/array-recheck.txt)仍未通过：交互提示已进入确认步骤，但自动化屏幕采样未检测到绿色复制预览。同一测试在 0.1.5 基准提交 `8766f61` 的干净工作树中也以相同断言失败，因此不能归因于本轮属性面板修改；该视觉用例仍待进一步确认，不计入本轮属性窗口的 13 个用例。

本轮改动限定于属性展示、共享样式和视图组合，未修改实体几何或属性命令语义。未穷举所有语言、DPI 与主题组合，也未重复运行完整绘制和 DXF 回归。窗口帧率不作为本轮性能证据。已打开的旧程序需要重启才能加载新界面。

## 源码与记录

[MD 共享样式](../Direct2dCad.wpf/Views/Toolboxes/EntityProperty/PropertyInspectorStyles.xaml) · [通用标签与颜色内容](../Direct2dCad.wpf/Views/Toolboxes/EntityProperty/PropertyInspectorCommonStyles.xaml) · [实体视图](../Direct2dCad.wpf/Views/Toolboxes/EntityProperty) · [标注参数](../Direct2dCad.wpf/Views/CadAnnotationParametersView.xaml)

[验收清单与哈希](code-review/property-visual-2026-10-08/verification.json) · [原绑定比对](code-review/property-visual-2026-10-08/binding-preservation.json) · [窗口用例最新结果](code-review/property-visual-2026-10-08/ui-latest-results.json)

上一轮参数排序和折叠布局的记录保留在 [property-ui 基线](code-review/property-ui-2026-10-08/verification.json)，不代表本轮最终构建。
