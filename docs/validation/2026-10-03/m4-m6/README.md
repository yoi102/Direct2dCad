# M4–M6 本机验证

日期：2026-10-03；本地未提交工作区，基线 `041e291287e93429d49f498b5ca571f9a8f12427`。SDK 10.0.401，.NET 10.0.12，Windows x64。与 [M1–M3 证据](../README.md) 分开，不复用其通过数量。

## 构建和回归

Release：0 警告、0 错误。所有项目依赖锁还原通过。最终按每个项目最新完整 TRX 汇总：**1461/1461**，失败 0，包含托管 1319、Windows integration 132、真实窗口 UI 10。剪贴板图片/OLE 用例明确排除，未改写用户系统剪贴板。精确项目计数见 [regression-summary.json](regression-summary.json)；原 TRX 在 `TestResults/m4-m6-final-regression`，不累加修复前的失败或专项重复运行。

UI 覆盖原横向状态栏、网格类型/主次间距、图标开关与键盘、绘图辅助重开、标注预选→Ribbon→精确放置→属性替代文字→撤销→模板。截图见 [标注属性](annotation-properties.png) 与 [900×700 绘图界面](drawing-assistant-900x700.png)。未把截图当作完整高 DPI/人工制图验收。

模型/命令/IO/工作流测试覆盖七种标注、关联更新/失效保留、独立值、字体箭头、复制与变换历史、跨文档块内引用、grip 与重新关联、版本分页/取消、历史字节预算、压缩副本保留原稿和撤销依赖。原生测试补上首屏不等待未调度/删除对象或 offscreen 捕获。

## 交换与打印

[独立 DXF 校验](dxf-external-audit.json) 使用 ezdxf 1.4.3 读取 [校准图](calibration.dxf) 和 [外部样图往返](external-roundtrip.dxf)，两个文件均无错误、无自动修复。校验 100 mm 线、隐藏实体、Unicode 转义、图层真彩色、块与非均匀插入；外部样图的线/圆弧几何与源文件在 1e-7 舍入后完全一致。源文件 provenance、MIT 和固定 SHA256 见 [来源](../../../samples/dxf/ezdxf-source.json)。校验器为 `scripts/testing/Validate-DxfExchange.py`，不修复输入文件。

Microsoft Print to PDF 的驱动票据/预览通过，**submitted=false**。actual/fit/custom 比例、A3→A4 裁切、模型当前范围和 1:100 下 2.5 mm 纸面注释换算见 [驱动记录](pdf-driver-preview.json) 与 [裁切预览](a3-on-a4-clipping-preview.png)。未提交 PDF 或真实打印作业；可选择文字、100 mm 实际文件/纸面测量仍待现场验收。

## 容量对照

[capacity.json](capacity.json) 记录机器、运行时、18 次独立场景运行和限制。100/20000/100000 mixed 实体，1600×900 离屏 Direct2D，100×56.25 mm 可见视区。每种模式各三次，下表为中位数；同步 geometry 准备和 bounded 可见优先使用相同图纸、视区与渲染选项。

| 实体数 / 准备策略 | 可见首 Present ms | 全部资源完成 ms | owner 调度 P95 ms |
| --- | ---: | ---: | ---: |
| 100 / 同步全准备 | 218.88 | 219.38 | 20.57 |
| 100 / 可见优先 | 211.35 | 211.52 | 16.08 |
| 20000 / 同步全准备 | 112.02 | 148.41 | 15.84 |
| 20000 / 可见优先 | 108.66 | 173.53 | 18.24 |
| 100000 / 同步全准备 | 349.44 | 495.74 | 45.09 |
| 100000 / 可见优先 | 299.46 | 569.62 | 28.27 |

采集取消后停止约 0.109 ms，取消保存未产生文件。这是协作采集停止，不是驱动/native 可抢占证明。十万实体可见优先第三次 working set 约 532 MiB；同进程累计 peak 约 596 MiB，不能当作每种模式独立峰值比较。

测量包含解码、空间索引和 host 创建。小图冷启动/设备初始化开销明显；heartbeat 样本量较少时 P95 不适合作稳定门槛。heartbeat 是调度延迟，未测物理鼠标；峰值累计，混合样图不含大型光栅/OLE。当前默认 chunk 仍关闭。前一轮并行回归时的试测与使用 Task.Delay 的试测未纳入此结果。

## 分发与未执行门槛

版本 `0.6.0-preview.1` 为未签名、本机自包含 ZIP，最终文件为 `TestResults/m6-release.zip`，完整 manifest 包含依赖、产物哈希、基线与未提交/未跟踪源文件哈希。manifest 记录构建时快照，后续证据和文档收尾不在其中。最终包启动与标注操作 **2/2** 通过，不累加到 1461 项完整回归；隔离安装→升级→回滚→拒绝篡改包→卸载全部通过，外部设置/恢复文件标记未改变。详见 [产物记录](delivery-artifact.json)、[生命周期记录](package-lifecycle.json) 和 [分发说明](../../../DELIVERY.md)。隔离测试保留用户真实 Programs、设置和恢复目录。

干净机器、受控签名/时间戳、公开发布、线上 CI、实际打印、目标 CAD 人工打开、多 GPU/多屏 DPI、真实大型光栅/OLE 交互未执行。它们不由本机回归数量或配置文件存在替代。
