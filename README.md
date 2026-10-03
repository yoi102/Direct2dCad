# Direct2dCad

[中文](README.md) | [日本語](README.ja.md) | [English](README.en.md)

Direct2dCad 是基于 WPF、Direct2D 和 DirectWrite 的 Windows 二维 CAD 编辑器。项目已有绘图、编辑、图层/块、布局视口、打印和原生文件保存链路，仍处于完善工程制图能力与交付可靠性的阶段。

## 文档导航

| 要了解的内容 | 文档 |
| --- | --- |
| 能做什么、还欠缺什么、先做什么 | [CAD 能力与不足](docs/CAD-READINESS.md) |
| 分阶段任务、简洁交互及验收标准 | [开发规划与交互验收](docs/ROADMAP.md) |
| M1–M3 当前实现、边界和验证 | [实施状态](docs/M1-M3-STATUS.md)、[绘图与编辑操作](docs/DRAWING-AND-EDITING.md) |
| M4–M6 标注、交换、容量与分发 | [实施状态](docs/M4-M6-STATUS.md)、[操作说明](docs/ANNOTATION-AND-EXCHANGE.md)、[本机分发](docs/DELIVERY.md) |
| 分层、项目职责、实际引用与 NuGet 依赖 | [架构与项目职责](docs/ARCHITECTURE.md) |
| Terminal 简写、AI 工具、执行记录与剩余边界 | [命令行与 AI](docs/COMMANDS-AND-AI.md) |
| 增量渲染、后台准备、资源边界及基准运行 | [性能、渲染与基准](docs/PERFORMANCE.md) |
| 回归脚本、覆盖范围、TRX、覆盖率及人工验收 | [测试说明](scripts/testing/README.md) |
| 2026-09-05 的覆盖率采样与修复记录 | [覆盖扩展记录](scripts/testing/COVERAGE.md) |
| 2026-09-05 的性能优化与短基准 | [性能记录](scripts/testing/PERFORMANCE-2026-09-05.md)、[增量优化记录](scripts/testing/PERFORMANCE-INCREMENTAL-2026-09-05.md) |

历史报告保留其日期、代码与机器边界；当前状态见 **2026-10-03** 的 [M1–M3](docs/M1-M3-STATUS.md) 和 [M4–M6 实施记录](docs/M4-M6-STATUS.md)，2026-10-02 的能力报告保留审查基线和问题依据。

## 当前功能

| 功能 | 已实现的范围 |
| --- | --- |
| 二维图形 | 直线、圆、圆弧、可旋转椭圆/椭圆弧和矩形、多段线、多边形、插值样条；模型另支持混合路径 |
| 文字与外部内容 | TrueType 文字、笔画文字、图像、OLE 对象 |
| 外观 | 图层颜色/线宽、显式颜色、描边端帽/虚线/连接、实色/渐变/图案填充；选中实体按能力显示属性 |
| 绘制属性 | 按工具类型保留当前文档会话的描边默认值，设置应用到预览和新建图形；颜色可取消“随图层” |
| 编辑 | 点选、框选/跨选、重叠候选切换、选择过滤、多实体属性、grip、移动/旋转/镜像/缩放；具体变换受实体类型限制 |
| 精确绘图 | 画布动态数字输入、精确坐标、对象/网格捕捉、正交/极轴；状态栏集中视图和捕捉设置，保持一行 |
| 曲线编辑 | 偏移、修剪、延伸、圆角/倒角、连接、打断、矩形/环形阵列；精确编辑范围为直线/圆弧路径，详见实施边界 |
| 组织与布局 | 图层、嵌套块引用、块编辑、多文档、Layout 纸空间和模型视口 |
| 历史与剪贴板 | 文档命令和编辑器操作有各自历史入口，支持单条/批次撤销重做；跨文档复制粘贴携带依赖块与资源 |
| 文件与输出 | 原生 `.d2cad`、版本迁移、文件预算与覆盖冲突保护、未知 section 只读兼容副本、自动恢复、打印预览与 Windows XPS/矢量打印结果反馈 |
| Terminal 与 AI | 命令别名/帮助/历史/补全、坐标输入；LM Studio 和 Codex 共用可撤销的 CAD 工具 |
| 渲染 | Direct2D 缓存、局部刷新、后台准备、LOD、布局投影、设备失效后的重建 |

M1–M6 已补齐精确输入、对象捕捉、基础编辑、文件保护/恢复、七种关联标注、工程模板、比例打印预览、有界 DXF、可见优先首屏和本机分发。DWG、真实打印、多屏 DPI、干净机器和受控签名等专项仍保留，详见 [最新证据](docs/validation/2026-10-03/m4-m6/README.md)。

## 构建与启动

在仓库根目录执行。主客户端面向 **Windows x64**，目标框架为 `.NET 10`，`global.json` 固定 SDK 10.0.401（允许同特性带补丁升级），需要 Windows 桌面环境。部分 UI/DI 依赖仍是预发布包；本次没有运行云端 CI。

```powershell
dotnet build .\Direct2dCad.slnx -c Release
dotnet run -c Release --project .\Direct2dCad.wpf\Direct2dCad.wpf.csproj
```

本机测试结果见[2026-10-03 验证记录](docs/validation/2026-10-03/README.md)。构建通过不表示打印机、外部 OLE Server 或所有 GPU/多屏环境已验收。

```powershell
# 默认托管回归
.\scripts\testing\Run-Regression.ps1 -Configuration Release

# 采集覆盖率
.\scripts\testing\Run-Regression.ps1 -Configuration Release -CollectCoverage
```

原生绘制和 UI 回归可加 `-IncludeWindowsIntegration -IncludeUiAutomation`。UI 自动化需要可交互桌面；剪贴板用例只在专用测试桌面启用，详见[测试说明](scripts/testing/README.md)。

生成可供手动验收的本机目录：

```powershell
dotnet publish .\Direct2dCad.wpf\Direct2dCad.wpf.csproj -c Release -r win-x64 --self-contained true
```

这条命令生成发布文件，不代表安装、升级、签名或正式发版流程已经完成。

已发布 [GitHub Release 0.0.0.2](https://github.com/yoi102/Direct2dCad/releases/tag/0.0.0.2)，含无需预装 .NET Runtime 的 x64 MSI 安装包和便携 ZIP；安装时会创建开始菜单与桌面快捷方式。版本、SHA256 和签名状态见[分发说明](docs/DELIVERY.md)。

## 基本使用

- 新建或打开 `.d2cad` 图纸，从工具栏或 Terminal 进入绘制模式，左键输入几何点。
- 绘制时通过属性面板选择目标图层、颜色、线宽、填充与适用的描边样式；图形专属参数随工具变化。
- 画布提供简短数字输入和候选切换；“图纸恢复”工具箱直接显示可恢复图纸，状态栏图标或 `Ctrl+Shift+D` 重新打开。底部保留坐标/单位、网格类型/主次间距，以及带文字提示的捕捉/约束图标开关。底栏右侧的“视图与捕捉”图标展开极轴角度、垂直/切线捕捉、原点/捕捉标记和背景；Esc 或点击面板外关闭，极轴角度支持 Enter 确认。
- `Enter` 完成多段线、多边形和样条等多点绘制；`Esc` 返回选择模式并清理绘制、选择框、grip 和粘贴预览。
- 选择模式先命中 grip。grip 移动/缩放采用预览后再次左键提交，松开鼠标只释放捕获。
- 右键或中键平移，滚轮缩放；支持选择框、跨选、候选切换和过滤。
- 欢迎页隐藏绘制、修改和标注页签，切回图纸恢复。Terminal 的 `HELP` 查看命令帮助，`TOOLS` / `TOOLHELP` 查看 JSON 工具；支持变换/布尔简写及坐标输入。单位约定、AI 调用和日志范围见[命令行与 AI](docs/COMMANDS-AND-AI.md)。
- 块、布局和模型视口有独立操作入口；Polygon 在数据库中是闭合的 `CadPolyline`。

24 种实际绘制模式覆盖直线、矩形、4 种圆、11 种圆弧、3 种椭圆/椭圆弧、多段线、多边形、样条和文字。插块、布局视口和原点放置是额外工具模式。

## AI 连接

AI 工具箱的齿轮按钮打开连接配置。LM Studio 默认地址为 `http://localhost:1234/v1`，需启动 Local Server 并加载支持 tool calling 的模型；Codex 使用本机 `codex app-server` 和 CLI 登录状态。

工具通过稳定的 `document_id` 路由工作区文档，可查询和编辑实体、管理图层/样式/块、打开与保存图纸。同一用户请求对各目标文档分别使用撤销批次。可用能力与实体变换边界以工具契约和源码为准；AI 入口不能补足尚未实现的 CAD 几何功能。

## 演示与设计

- [基本操作演示 1](https://github.com/user-attachments/assets/53180795-5870-42c7-9148-5586ca1bfd6b)、[基本操作演示 2](https://github.com/user-attachments/assets/5515d18a-1d88-4851-a8d9-54f10bdee5ed)
- [Block 演示](https://github.com/user-attachments/assets/45c5e49e-c59a-4f80-aaf3-de8ec7680310)
- [Layout 演示](https://github.com/user-attachments/assets/847600ec-c82e-4ed0-82d9-443d59339906)
- [OLE 演示](https://github.com/user-attachments/assets/ab1f207f-48c2-40a8-b698-496c6077a0a3)
- [Terminal 演示](https://github.com/user-attachments/assets/fc7236e2-93e8-44f3-800d-b00bfd54f761)
- [LM Studio AI 演示 1](https://github.com/user-attachments/assets/ebb26f5b-63a1-4159-a101-69da56e776a7)、[AI 演示 2](https://github.com/user-attachments/assets/63a6763b-b63c-4a29-a499-cadb94242509)
- [Figma 设计稿](https://www.figma.com/board/wZWqWgQ9dd1p4KQVBakqmS/Direct2dCad?node-id=52-299&t=jXGAkAOnYQmodsTk-4)

## 许可证

见 [LICENSE.txt](LICENSE.txt)。
