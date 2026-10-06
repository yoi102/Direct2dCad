# 应用层与无界面工具执行

## 实现

- 新增 `Direct2dCad.Application`，将文档/工作区工具、查询、几何、样式、图层、批量创建及工具命令行从 ViewModels 迁入。无 ViewModels、Services、WPF、AvalonDock 或 Direct2D 依赖。
- `ICadToolWorkspace` 的结果包含 `ICadWorkspaceDocument` 和 `ICadToolDocumentSession`；接口不再暴露 `EditorTabViewModel` / `CadDocumentViewModel`。
- 文档宿主负责加载、操作生命周期和取消；会话负责编辑器、活动空间和交互状态。桌面 ViewModel 用 partial 适配这些契约，工具实现无需识别具体窗口类型。
- 提供 `CadToolDocumentSession` 用于无界面的模型空间编辑。进入用户块前检查系统/只读状态，失败保持当前 owner 和选择不变。已关闭会话不能通过保留的工具执行器继续修改。
- 图片导入与工具活动消息移到应用契约；填充选项共用同一目录，避免 UI 和工具分别维护默认图案。
- 桌面 `CadToolWorkspace` 使用每文档工厂；关闭时显式释放标签，确保不依赖 WPF 关闭事件。取消关闭保留会话。名称初始化在工厂失败清理范围内。
- 文档工具仍通过现有命令系统执行；文档之间 undo batch 独立；查询继续使用不可变快照、版本/owner/选择/关闭检查。
- 布尔运算的异步准备结束后重新检查宿主、会话存活及编辑器身份。等待期间关闭或替换图纸会拒绝提交，旧图和新图都不会被晚到结果修改。
- 无界面工具的自动结果选择与桌面一致：过滤活动空间和可选实体，直接更新选择集；只有显式 `select_entities` 才写入编辑器历史。工具模式统一返回 `Select`。

## 使用方式

脚本可以直接创建 `CadToolDocumentSession(CadDocument.Create(...))`，再创建 `CadDocumentToolExecutor(session, batchId)`；工作区宿主实现 `ICadToolWorkspace` 后使用 `CadWorkspaceToolExecutor`。后者提供异步快照查询、取消和多文档路由。宿主应在所属线程提交编辑，并负责释放会话。

迁移后的公开类型命名空间是 `Direct2dCad.Application.Tools` 与 `Direct2dCad.Application.Platform`。图片导入、工具目录、工作区 DTO 的旧 ViewModels 命名空间不再是入口。

## 验证

新增 Application.Tests 只引用 Application 项目。已通过 11 个无界面用例：

- 两张文档编辑、独立撤销/重做及查询。
- 旧版本查询拒绝、取消不返回部分结果。
- 会话关闭后执行器拒绝写入。
- 应用程序集和工作区公开契约无界面/原生后端依赖。
- 只读块进入失败后 owner 与选择保持不变。
- 自动选择不额外写入历史，显式选择保留撤销；无效和其他空间的实体不会进入自动选择。
- 布尔准备期间替换编辑器、关闭会话或移除宿主时拒绝提交；正常异步提交仍可撤销。

新增真实 DI 工作区关闭测试覆盖取消关闭和确认关闭，没有 WPF 关闭事件也会释放文档。原 ViewModels 工具行为测试保留，测试辅助代码显式取得桌面 ViewModel，不将该类型放回应用契约。

完整构建和最终回归结果见 [总验收文档](../ARCHITECTURE-OPTIMIZATION.md)。
