# M6 分发与 GitHub Release

2026-10-03 发布 `0.0.0.2`。仅提供 Windows x64。安装器和便携包均为 .NET 10 自包含版本，程序目录随包包含 .NET 与 Windows Desktop 运行时，不要求目标电脑预先安装 .NET Desktop Runtime。

## 下载

- [Direct2dCad.msi](https://github.com/yoi102/Direct2dCad/releases/download/0.0.0.2/Direct2dCad.msi) — 安装到 Program Files，并创建开始菜单与桌面快捷方式。程序、MSI 属性及快捷方式都使用项目图标。
- [Direct2dCad.zip](https://github.com/yoi102/Direct2dCad/releases/download/0.0.0.2/Direct2dCad.zip) — 解压后运行 `Direct2dCad.exe`。
- [SHA256SUMS.txt](https://github.com/yoi102/Direct2dCad/releases/download/0.0.0.2/SHA256SUMS.txt) — 校验发布文件。

GitHub Release 页面：[Direct2dCad 0.0.0.2](https://github.com/yoi102/Direct2dCad/releases/tag/0.0.0.2)。对应的 MSI 内部产品版本为 `1.0.2`，符合 Windows Installer 三段版本规则并高于原 `1.0.0` 安装项目；`ARPVERSION`、主程序文件版本和 GitHub 标签保留 `0.0.0.2`。

此构建未签名。Windows 可能显示未知发布者；项目没有配置代码签名证书，发布内容不声称已签名。

## 重建

仓库固定使用 .NET SDK 10.0.401。PowerShell 从干净仓库根目录运行：

```powershell
./scripts/delivery/Build-GitHubRelease.ps1
```

脚本在新的 `TestResults/github-release-0.0.0.2` 目录里发布 win-x64 自包含程序，打包 MSI 和 ZIP，并生成校验和与 `release-manifest.json`。如果该目录已经存在，可传入新的 `-OutputDirectory`。发布产物不会写进源码目录。

## 验证状态

- Release 自包含发布成功，runtimeconfig 明确列出随程序包含的 `Microsoft.NETCore.App` 与 `Microsoft.WindowsDesktop.App`。
- WiX 6 Release 安装包构建成功，0 警告、0 错误；MSI 数据库包含产品图标、ARPVERSION、开始菜单／桌面快捷方式和程序文件。
- 托管与 Windows UI 回归在本次源码更改前已由本机通过，记录在 [2026-10-03 验证](validation/2026-10-03/README.md)；M6 阶段目录包还通过了隔离目录下的启动和安装脚本生命周期检查。上述结果不等同于本次 MSI 的实机安装、升级或卸载测试。
- 本次 Release 文件和哈希见 [发布清单](validation/2026-10-03/github-release/verification.json)。
- 未签名、未在干净 Windows 电脑验收，也未运行云端 CI；真实打印机、外部 OLE Server、DWG 与完整 DXF 兼容保持为单独的验证／开发项。
