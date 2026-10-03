# 图纸恢复的选择、打开与列表样式

2026-10-03，Windows，Release。本次所有窗口检查均使用隔离的临时 settings／recovery 目录。

## 行为

- Documents 和图纸恢复使用同一列表项模板：左侧 2 px 主题色竖条表示选中，主题前景色以 8% 不透明度形成悬停背景，主题主色以 12% 不透明度形成选中背景。覆盖层与正文分离，文字和图标保持原有清晰度；深浅主题均可用。
- 恢复条目的名称区域只选中，不直接打开。右侧恢复按钮和右键菜单打开项执行恢复；键盘选中条目后可 Tab 到按钮并按空格打开。
- 再次打开同一来源图纸的恢复稿，激活现有 EditorTab，不重新加载或新增 Documents。按来源 DocumentId 匹配，名称相同不影响判断；已编辑内容保留。保存或明确关闭后原有恢复稿清理流程继续生效。
- View、ViewModel、MainViewModel 属性／命令及标题资源改名为 DrawingRecovery。保留 `toolbox.drawing-assistant` 布局 ID 与原有入口 AutomationId，兼容已保存的停靠布局。
- 列表容器使用本地共享模板，右键菜单继续从 PlacementTarget 取得条目及命令上下文。未恢复存在祖先查找错误的主题容器样式，也未降低绑定诊断等级。

## 验证

- 全解决方案 Release 构建：0 warning、0 error。
- 针对性 ViewModel 测试：11/11，包含来源 ID 复用、相同名称不同图纸、保持编辑内容、正常关闭／取消／保存失败及备份写入竞争。
- 最终窗口测试：10/10，覆盖中文深色、英文浅色、900×700／1300×900、恢复条目只选中、再次恢复与切回已有图纸、右键菜单、右侧按钮、单项／全部清除、路径提示、Tab／空格恢复、滚动、欢迎页和 Ribbon 状态。
- 人工查看最终截图，确认两列表的选中和悬停背景可区分、竖条可见、正文清晰。10 份最终绑定日志均为空。

原始证据位于 `TestResults/recovery-list-interaction`。最终结果使用 `recovery-workflow.trx` 和 `list-ui-final.trx`；截图及绑定日志位于 `final-screenshots`。目录内其他 TRX 为中间诊断，保留修复前的标题、路径提示和焦点同步问题，不计为最终结果。文件哈希、结果计数及绑定日志清单见 [verification.json](verification.json)。

本次未重复真实异常终止流程；此前的恢复生命周期验证见 [恢复生命周期](../recovery-lifecycle/README.md)。
