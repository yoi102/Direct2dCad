# Direct2dCad

[中文](README.md) | [日本語](README.ja.md) | [English](README.en.md)

Direct2dCad 是面向 Windows 的二维 CAD 编辑器，基于 WPF、Direct2D 和 DirectWrite 构建。它将绘图、精确输入、图形编辑、标注与图纸管理放在同一个桌面工作区中，并提供命令行和 AI 辅助操作。

## 主要功能

- **二维绘图**：直线、多段线、多边形、矩形、圆、圆弧、椭圆、椭圆弧和样条，以及文字、图像和 OLE 对象。
- **精确输入**：在画布上直接输入坐标、长度、半径、直径、角度和椭圆半轴；支持对象捕捉、网格捕捉、正交与极轴约束。
- **图形编辑**：点选、框选、跨选、选择过滤和控制点编辑；移动、旋转、镜像、缩放，以及适用曲线的偏移、修剪、延伸、圆角、倒角、连接、打断和阵列。闭合轮廓支持布尔并集、交集与差集。
- **外观与标注**：颜色、线宽、虚线、端帽、连接样式、填充和图案；水平、垂直、对齐、半径、直径、角度标注与引线，可调整字体和箭头样式。
- **图纸组织**：多文档、图层、块与嵌套块引用、块编辑、布局和模型视口，支持撤销重做与跨图纸复制粘贴。
- **文件与输出**：原生 `.d2cad` 文件、常用二维实体的 DXF 导入导出、自动恢复、打印预览和比例打印。
- **命令行与 AI**：Terminal 提供命令帮助、补全和历史；可连接 LM Studio 或 Codex 查询图纸、创建图形和执行可撤销的编辑操作。

界面支持中文、日语和英语，提供明暗主题、可停靠工具箱和可配置的径向菜单。

## 下载与安装

从 [GitHub Releases](https://github.com/yoi102/Direct2dCad/releases) 下载 Windows x64 版本：

- **MSI 安装包**：安装应用并创建桌面与开始菜单快捷方式。
- **便携 ZIP**：解压后运行 `Direct2dCad.exe`。

两种方式均包含所需的 .NET Runtime，无需另外安装运行时。

## 快速开始

1. 新建图纸或打开 `.d2cad` / DXF 文件。
2. 在“绘制”页选择工具，通过鼠标指定几何点，或直接在画布的短数值框中输入参数。
3. 选择图形后，在属性面板调整图层和外观，使用“修改”页编辑，或在“标注”页添加尺寸。
4. 保存为 `.d2cad`，也可以导出 DXF，或通过布局和打印预览输出图纸。

常用操作：

- `Tab` / `Shift+Tab` 切换数值字段；`Enter` 确认当前输入，多点绘制接受最后一点后再按 `Enter` 完成。
- `Esc` 取消当前操作并返回选择模式。
- 滚轮缩放，右键或中键平移；控制点编辑先预览，再次左键确认。
- 在 Terminal 输入 `HELP` 查看命令，输入 `TOOLS` / `TOOLHELP` 查看 AI 工具。

## AI 连接

在 AI 工具箱中点击齿轮按钮配置连接：

- **LM Studio**：启动 Local Server，并加载支持工具调用的模型。默认地址为 `http://localhost:1234/v1`。
- **Codex**：通过本机 Codex CLI 的登录状态连接 `codex app-server`。

AI 可查询实体、图层和块，创建或修改图形，并打开、保存和切换图纸。编辑结果进入图纸的撤销历史，可继续手动调整。

## 从源码运行

开发者文档：[架构与项目职责](docs/ARCHITECTURE.md) · [架构优化实施与验收](docs/ARCHITECTURE-OPTIMIZATION.md)。

需要 Windows x64 和 .NET 10 SDK。仓库的 `global.json` 指定 SDK 10.0.401，并允许同特性带的补丁升级。

在仓库根目录执行：

```powershell
dotnet build .\Direct2dCad.slnx -c Release
dotnet run -c Release --project .\Direct2dCad.wpf\Direct2dCad.wpf.csproj
```

生成包含运行时的发布目录：

```powershell
dotnet publish .\Direct2dCad.wpf\Direct2dCad.wpf.csproj -c Release -r win-x64 --self-contained true
```

## 演示与设计

- [基本操作演示 1](https://github.com/user-attachments/assets/53180795-5870-42c7-9148-5586ca1bfd6b)
- [基本操作演示 2](https://github.com/user-attachments/assets/5515d18a-1d88-4851-a8d9-54f10bdee5ed)
- [基本操作演示 3](https://github.com/user-attachments/assets/bc4d198b-6fc4-41e9-8b05-926b85c0c560)
- [基本操作演示 4](https://github.com/user-attachments/assets/f5e29d71-08de-4a61-8dda-ee3dab1f81be)
- [Block 演示](https://github.com/user-attachments/assets/45c5e49e-c59a-4f80-aaf3-de8ec7680310)
- [Layout 演示](https://github.com/user-attachments/assets/847600ec-c82e-4ed0-82d9-443d59339906)
- [OLE 演示](https://github.com/user-attachments/assets/ab1f207f-48c2-40a8-b698-496c6077a0a3)
- [Terminal 演示](https://github.com/user-attachments/assets/fc7236e2-93e8-44f3-800d-b00bfd54f761)
- [LM Studio AI 演示 1](https://github.com/user-attachments/assets/ebb26f5b-63a1-4159-a101-69da56e776a7)、[AI 演示 2](https://github.com/user-attachments/assets/63a6763b-b63c-4a29-a499-cadb94242509)
- [Figma 设计稿](https://www.figma.com/board/wZWqWgQ9dd1p4KQVBakqmS/Direct2dCad?node-id=52-299&t=jXGAkAOnYQmodsTk-4)

## 许可证

本项目采用 [MIT License](LICENSE.txt)。
