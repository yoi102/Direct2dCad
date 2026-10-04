# 2026-10-04 · Direct2D 绘制优化前后对照

已修复绘制回调异常后原生批次无法继续、视区变化后可见准备等待集合过期的问题，并优化宽线瓦片缓存、平移预取和连续缩放的 realization 策略。两份用户图纸中，最大的收益是消除平移时的缓存缺失长帧，以及让第二份图纸从每帧大量提交转为瓦片回放。第一份图纸的连续全量缩放仍有约 192 ms 的 P95，不能称为已经发挥 Direct2D 的全部性能。

## 数据与环境

输入只读，完成后重新计算 SHA256，原文件均未变化。实体数量包含块定义实体及已擦除实体；测试显示模型空间。

| 输入 | 文件字节 | 实体数 | SHA256 |
|---|---:|---:|---|
| `pUntitled.d2cad` | 539,837 | 27,365 | `EB95124FF72B90525155C57F3F91EB275868B01122E7CC9666352279A5D487C5` |
| `p2Untitled.d2cad` | 2,336,311 | 50,410 | `BEB168E79972CC421FBFE68475329A424DB247452C8974D3580D699B2145612E` |
| `ArduinoUnoRev3PCB.dxf` | 3,585,205 | 导入 1,681 | `3354C72BFCEF80571DBB617D95D889780232557EFECB718EF73D48B769106891` |

Windows 11 Pro for Workstations Insider Preview 10.0.26220，i7-13700K。机器安装 RTX 5070 Ti / Intel UHD 770；本次没有单独记录实际选择的适配器名称。全部测量 `UsingWarp=false`，使用硬件 Direct2D。`.d2cad` 脚本运行时为 .NET 10.0.11，程序集为 Release `net10.0-windows`。

保留修改前的可执行目录，前后分别在新的 STA PowerShell 进程加载指定目录，没有用新编译文件覆盖基线。基线代码为 `82ee15adf5355a7d504dae7a4242713614782635`；优化版程序集哈希见下表。

| `Direct2dCad.Rendering.Direct2D.dll` | SHA256 |
|---|---|
| 修改前 | `9706DF38ED2B542F76CA1694A3E6D64206D228176A70D21C3DB1A9B359943DA1` |
| 修改后 | `AF913D97A3F702069FB38EFA13336735A1C6A982D4513D165E92BBC65078187F` |

## 测量方式

`.d2cad` 各加载三次，每种场景每次 64 帧，共 192 帧，1600×900、LOD 开启、无网格和原点。先完成几何/场景缓存准备并预热，再测以下操作：

- **全图重绘**：强制更新基础场景，覆盖窗口完整区域。
- **局部覆盖层**：恢复 240×120 区域并重画覆盖层；场景没有选中实体或 transient，这是恢复/呈现路径成本，不代表复杂选中高亮成本。
- **平移**：连续 32 次向右 4 px，再 32 次返回；每次强制全量绘制。
- **缩放**：以窗口中心连续放大 32 次，每次 1.1 倍，再缩小 32 次返回；每次强制全量绘制。

计时包含视图操作、原生提交、`EndDraw` 和共享纹理 Present 回调；PowerShell 调用开销同样存在于前后版本。没有 GPU timestamp、实际 WPF D3DImage 合成、鼠标输入、显示器刷新或 Dispatcher 节奏。画布已有交互快照预览，本次全量缩放轨迹不模拟那条路径。

中位和 P95 使用排序后 `floor((N-1)*比例)` 取样。均值、最大值和逐帧数据一并保留，避免用中位隐藏长帧。所有场景顺序相同且 GPU 测量串行执行。第一份基线额外复测，最终表采用复测结果；初次基线的平移 P95 166.72 ms、缩放 P95 191.65 ms，与复测趋势一致。

## 绘制耗时

单位 ms，表格完整保留改善及退化项目。

| 图纸 | 场景 | 前中位 | 后中位 | 前均值 | 后均值 | 前 P95 | 后 P95 |
|---|---|---:|---:|---:|---:|---:|---:|
| pUntitled | 全图重绘 | 0.111 | 0.151 | 0.142 | 0.224 | 0.252 | 0.227 |
| pUntitled | 局部覆盖层 | 0.071 | 0.077 | 0.150 | 0.107 | 0.165 | 0.142 |
| pUntitled | 平移 | 0.163 | 0.145 | 44.132 | 0.169 | 167.092 | 0.232 |
| pUntitled | 缩放 | 3.249 | 3.710 | 47.709 | 47.672 | 196.184 | 191.648 |
| p2Untitled | 全图重绘 | 706.705 | 0.216 | 690.051 | 0.240 | 814.175 | 0.347 |
| p2Untitled | 局部覆盖层 | 0.267 | 0.143 | 0.336 | 0.213 | 0.457 | 0.249 |
| p2Untitled | 平移 | 716.995 | 0.193 | 712.777 | 0.240 | 813.459 | 0.302 |
| p2Untitled | 缩放 | 33.820 | 1.899 | 196.415 | 49.001 | 760.966 | 203.527 |

第一份平移 P95 降低约 99.86%；第二份全图重绘中位降低约 99.97%，缩放均值降低约 75.05%、P95 降低约 73.25%。第一份缩放均值几乎未变，中位反而增加约 0.46 ms；其即时提交长帧仍需后续优化。已经很快的第一份热缓存重绘中位增加约 0.04 ms，不能宣称每种场景都变快。

最大值分别如下，偶发调度/JIT 停顿仍包含在记录中：

| 图纸 | 场景 | 前最大 | 后最大 |
|---|---|---:|---:|
| pUntitled | 全图重绘 / 局部覆盖层 | 0.421 / 7.441 | 14.362 / 3.925 |
| pUntitled | 平移 / 缩放 | 171.461 / 225.063 | 3.014 / 233.703 |
| p2Untitled | 全图重绘 / 局部覆盖层 | 852.201 / 7.575 | 0.640 / 5.790 |
| p2Untitled | 平移 / 缩放 | 965.017 / 825.669 | 3.472 / 235.587 |

## 首屏与完整准备

下面分列三次原始耗时，单位 ms。第一次包含进程内首次调用/JIT/设备初始化，后两次是同进程中的重复打开，没有将它们统一称为冷启动。解码单独计时；首 Present 从解码和 fitted-document descriptor 创建完成之后开始，包含 viewport、空间索引、设备和资源准备，不是完整 GUI 打开耗时。准备循环使用 owner `Thread.Yield`，不模拟 WPF 调度。

| 图纸版本 | 解码 1 / 2 / 3 | 首可见 Present 1 / 2 / 3 | 全部资源完成 1 / 2 / 3 |
|---|---|---|---|
| pUntitled 前 | 266.4 / 127.9 / 159.3 | 818.8 / 375.7 / 386.0 | 1213.8 / 703.0 / 703.0 |
| pUntitled 后 | 257.0 / 121.6 / 161.6 | 839.1 / 364.0 / 387.4 | 1222.9 / 685.0 / 733.0 |
| p2Untitled 前 | 443.8 / 578.3 / 457.6 | 1239.6 / 1641.4 / 1262.9 | 1916.4 / 2587.0 / 2184.7 |
| p2Untitled 后 | 461.3 / 287.6 / 311.6 | 1228.1 / 633.0 / 615.7 | 2515.7 / 1711.6 / 1702.2 |

首屏早于全部资源完成。第二份优化后第一次完整准备更久，包含新增的瓦片准备；本次没有修改解码实现，不能将解码波动归因于渲染优化。

## 资源成本与原因

第二份基线的粗线权重使保守的 64 px 瓦片描绘范围保护禁用整张图的瓦片缓存。预热全图仍有约 36,960 次实体提交，约 6,850 次细碎 command list 回放，另有填充/块 fallback；优化后全图重绘中位为 12 次瓦片回放、0 次实体提交。

将可缓存描绘范围扩大到半个瓦片（256 px），并同步扩大编辑失效余量，保留超大描边的即时兜底。绘制仍使用既有宽线空间查询扩边；初始准备也扫描未捕获实体的线宽，防止可见宽线被遗漏。瓦片在可见区域之后准备相邻一圈，只有总数不超过每 profile 64 个时才预取，现有全局缓存预算继续生效。

| 成本 | pUntitled 前 → 后 | p2Untitled 前 → 后 |
|---|---|---|
| 轨迹中估算缓存峰值 | 19.95 → 38.09 MiB | 35.70 → 65.55 MiB |
| 全图帧托管分配中位 | 5,216 → 5,576 B | 1,026,640 → 5,200 B |
| 平移帧托管分配中位 | 6,984 → 7,448 B | 1,028,512 → 7,072 B |

这些是现有诊断中的缓存估算，包含纹理/command list 等估算值，不是实际总显存或进程峰值内存。预算为 512 MiB；本次未测量实际 VRAM、进程峰值或后台预取期间的物理输入延迟。

此外，普通未旋转实体不再重复设置原生变换；同设备基础场景捕获/恢复移除冗余 Flush，最终共享 WPF 表面 Present 和交互快照 Flush 保留。当前模型 zoom 改变时绕过即时 realization 绘制/构建，静止帧及空闲准备恢复缓存，原有 command list/瓦片回放仍保留。

## PCB 缩放复测

516×384，同一右侧连接器锚点，三轮各 32 次放大和 32 次缩小。默认设置中位 2.680 → 1.657 ms（降低约 38.2%），P95 7.731 → 6.361 ms。连续轨迹中的 realization 构建归零；禁用 realization 的诊断路径约 0.994 ms，说明默认缓存/LOD 路径仍有额外成本。

DXF 导入只覆盖 1,681 个实体，**10,611 个 Wide polyline 仍不支持**。这项测量不能代表 DXF 文件的完整绘制或兼容性。

## 画面验证

新增原生测试对比同步完整准备与可见优先准备、6/12 线宽瓦片回放与即时绘制、编辑后失效以及跨边界预取回放的像素，均通过。模型视区变化、Layout 模型中心变化和宽线中心线在视区外的情况均覆盖。异常回调测试验证旧呈现不变、下一帧可继续。

真实图纸截图的前后像素差异保存在 [pixel-comparison.json](pixel-comparison.json)：

| 截图 | 不同像素 | 通道差大于 16 的像素 | 最大通道差 | 平均绝对通道差（0–255） |
|---|---:|---:|---:|---:|
| pUntitled 拟合 | 64 | 0 | 9 | 0.000056 |
| pUntitled 放大 32 次 | 0 | 0 | 0 | 0 |
| p2Untitled 拟合 | 184,012 | 48,389 | 134 | 0.796382 |
| p2Untitled 放大 32 次 | 0 | 0 | 0 | 0 |

第二份拟合画面新增了瓦片回放，使用已有的每倍频程 16 个缩放桶和线性重采样，桶内最大约 2.2% 的缩放差会产生抗锯齿/重采样差异。人工查看完整前后截图未见明显实体缺失，但这里**没有宣称真实图纸逐像素一致**，也没有替代用户的显示质量验收。

前后截图用于本地人工检查，因画面包含用户的 CAD 图纸内容，不随公开源码提交。像素比较数值仍保留在 [pixel-comparison.json](pixel-comparison.json)。

## 验证与剩余范围

- Release 全解决方案构建：通过，0 警告、0 错误。
- 原生绘制相关集成测试：210 通过，0 跳过，包含外部 PCB 样图。
- `EditCanvasInteractionTests` / `RenderingUserSettingsTests`：44 通过。
- `git diff --check`：通过。
- 实际 WPF 输入/合成延迟、GPU timestamp、多适配器对照：未运行。
- 可选后台 chunk 录制仍默认关闭，读取可变文档的线程风险未修复。
- 现有 UI FPS 仍由 CPU 平均绘制耗时倒数得到；本报告不将其作为实际显示 FPS。

## 复现与归档

汇总和版本哈希：[comparison.json](comparison.json)。逐帧耗时、分配、实体提交、瓦片/command 回放及 realization 计数：[frames.csv](frames.csv)。完整原始统计和保留的基线可执行文件位于本机仓库忽略目录 `artifacts/render-optimization-20261004/`，未包含在公开提交中。

在仓库根目录分别启动新进程运行：

```powershell
pwsh -NoProfile -STA -File scripts/benchmarks/Measure-RenderDocument.ps1 `
  -AssemblyDirectory artifacts/render-optimization-20261004/before-bin `
  -DocumentPath "$env:USERPROFILE\Downloads\pUntitled.d2cad" `
  -OutputDirectory artifacts/render-optimization-repeat/before -Label before

pwsh -NoProfile -STA -File scripts/benchmarks/Measure-RenderDocument.ps1 `
  -AssemblyDirectory Direct2dCad.Benchmarks/bin/Release/net10.0-windows `
  -DocumentPath "$env:USERPROFILE\Downloads\pUntitled.d2cad" `
  -OutputDirectory artifacts/render-optimization-repeat/after -Label after

# 换成 p2Untitled.d2cad 后重复上述命令。
$env:DIRECT2DCAD_UI_DXF_SAMPLE="$env:USERPROFILE\Downloads\ArduinoUnoRev3PCB.dxf"
dotnet test Direct2dCad.Windows.IntegrationTests -c Release --no-restore `
  --filter 'FullyQualifiedName~RenderOptimizationIntegrationTests|FullyQualifiedName~Direct2D|FullyQualifiedName~VisiblePreparation|FullyQualifiedName~VisiblePolylineStroke|FullyQualifiedName~ResourcePreparationBudget|FullyQualifiedName~LevelOfDetailPreparation|FullyQualifiedName~ParallelRenderPlanning|FullyQualifiedName~CacheEviction'
```

此记录汇总了 2026-10-04 发布前工作区的测量结果及其验证边界。
