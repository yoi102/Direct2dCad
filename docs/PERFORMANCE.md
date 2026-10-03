# 性能、渲染与基准

[返回首页](../README.md) · [架构](ARCHITECTURE.md) · [CAD 能力与不足](CAD-READINESS.md)

本文记录当前实现的资源更新原则、性能边界和基准运行方式。具体数值必须与代码版本、机器、驱动和测试场景一起读取；构建或回归通过不代表交互延迟或 FPS 达标。

## 2026-10-03 M1–M3 响应基线

[本机记录与限制](validation/2026-10-03/README.md#响应基线)包含固定 100 / 20000 / 100000 实体 mixed 样图，各测 3 次，原始数值和样图哈希已归档。

```powershell
dotnet run -c Release --project Direct2dCad.Benchmarks -- --m1-m3-baseline TestResults/m1-m3-baseline
```

测量分开记录解码加载、完整资源准备后的离屏首 Present、保存、独立快照后台查询、Dispatcher heartbeat P95/最大延迟、协作采集取消和累计进程峰值。三组完整准备后首 Present 中位为 223 / 424 / 1111 ms，owner 最大延迟约 1.0 / 0.64 / 1.0 秒。heartbeat 不是实际鼠标输入延迟；该首 Present 等待完整准备，不能称为渐进首屏验收。原生准备不可抢占，仍需 M6 优化。

## 2026-10-03 M6 可见优先对照

首帧现在只等待可见区域和所需嵌套定义；有界后台继续准备其他对象。worker 仍只使用独立值快照，可选 chunk 录制继续关闭。优先队列忽略新出现的未调度 ID，删除/失效对象及时退出等待集合。

```powershell
dotnet run -c Release --project Direct2dCad.Benchmarks -- --m4-m6-evidence TestResults/m4-m6-evidence-new
```

固定 100/20000/100000 实体，同一 100 × 56.25 mm 视区，对照同步全 geometry 准备和可见优先，各三次。分别记录解码、首个可见 Present、全部资源完成、owner 调度 P95/最大值、当前/累计峰值内存和协作取消。基准使用 Dispatcher Background yield，与画布准备循环一致；不能用 Windows 1 ms Task.Delay 的实际定时粒度作为准备速率。

十万实体首帧中位约 299 ms，同步约 349 ms；owner P95 中位约 28 ms，同步约 45 ms；完整资源中位约 570 ms，同步约 496 ms。优先策略改善首屏和调度，完整准备可能稍慢。原始环境与三次结果见 [最新证据](validation/2026-10-03/m4-m6/README.md)。heartbeat 不是物理输入延迟，峰值是同进程累计值，生成样图不包含大型光栅/OLE。

查询按 editor 和 DocumentChangeVersion 复用独立快照；分页返回 document_version，后页可传 expected_document_version，变更后拒绝旧版本。捕获仍有整图序列化/读取成本，深分页仍有 offset+limit 候选内存成本。历史按保守托管图估计字节裁剪，默认 256 MiB，最新完整批次允许超过软预算。

## 2026-10-03 Arduino PCB 局部缩放

真实 DXF 在右侧连接器处放大时，少量 bounds 跨越整图的长多段线仍整条参与描边，并在屏幕恒定线宽下频繁重建 realization。已加入有安全余量的可见描边段提交，保留实体、坐标、连接样式和完整填充；选择高亮复用相同逻辑。长实线不再冻结到全路径 command list，虚线保留完整 dash phase 和可复用录制。

同一硬件 Direct2D、516×384、相同导入结果和缩放轨迹，完整绘制中位从约 43 ms 降至约 3 ms；准确数值、原生像素差异、真实窗口滚轮截图与未覆盖范围见 [专项记录](validation/2026-10-03/dxf-zoom/README.md)。这些是绘制耗时，不代表物理显示 FPS；导入器仍有 Wide polyline 不支持的范围。

## 绘制和资源更新原则

- 实体创建、修改、删除后，命令结果应携带 `CadDocumentChangeSet`。
- Editor 根据 change set 更新选择、索引、bounds 和渲染资源。
- Direct2D 后端根据 change set 创建、更新或释放 geometry / brush / hatch / text layout 等资源。
- 原子命令批次立即更新空间索引和块边界，批次结束后合并 GPU 资源更新、文档通知与命令日志；单条历史仍保留，撤回粒度由当前设置决定。
- 绘制顺序同时考虑 layer drawing priority、实体 `ZIndex` 和实体加入顺序。
- 实体颜色、line weight 可以设置为跟随 layer；这种情况下实体自身属性仍可保存，但绘制时使用 layer 的最终外观。
- fill / hatch 的颜色应使用统一 fill color；hatch pattern 不应额外绘制不需要的背景色。
- Transient 绘制应尽量复用普通实体绘制规则，只把辅助线、测量文字、snap marker 作为额外 overlay。
- 局部刷新 dirty rect 需要考虑 geometry、line weight、fill / hatch、handle、transient preview 和旧位置/新位置的合并区域。

普通实体编辑只检查本次变化涉及的选中项；删除、创建块等按钮按选区和访问状态版本缓存可用性。嵌套块的纯外观变化会传播重绘通知，但不重算引用边界或更新空间索引。Layout 将模型变化映射到各个可见视口并裁剪脏区域，结构、视图设置及屏幕/纸空间线宽模式切换仍保留完整刷新的兜底。

Agent / Terminal 查询先协作获取独立值快照，再后台流式统计；返回前校验版本与存活状态。分页只保留最多 `offset + limit` 个排序候选并维持稳定顺序；深分页仍可能占用较多内存，快照采集有成本，统计仍需遍历查询范围。

并行绘制优先使用完整可用的 tile / command list 缓存；缓存不足时才查询可见实体并按估算绘制成本分配连续任务，保持绘制顺序。两种设备模式都只为 worker 实际负责过的实体准备资源，实体修改时增量更新，窗口尺寸变化只更换渲染目标；设备失效时仍完整重建。块面板复用列表项，并按块目录版本刷新，关闭期间延迟列表更新。

2026-09-05 的历史验证与基准范围见 [性能优化记录](../scripts/testing/PERFORMANCE-2026-09-05.md)。

块引用边界和大选择集支持增量更新，首次几何快照采用有界分批准备；实现边界与短基准见 [增量优化记录](../scripts/testing/PERFORMANCE-INCREMENTAL-2026-09-05.md)。

当前首次 Present 仍等待完整资源准备，尚未实现可见区域就绪即显示首屏；单个巨大实体操作不能被时间预算抢占，见[大图纸边界](CAD-READINESS.md#large-drawings)。可选后台 chunk 录制默认关闭，仍读取可变模型；它与使用值快照的 geometry 准备是两条路径，见[线程风险](CAD-READINESS.md#background-recording)。

## 性能基准

`Direct2dCad.Benchmarks` 使用 BenchmarkDotNet，并按性能边界拆成以下几组：

基准必须使用 `Release` 配置运行。在 Visual Studio 中启动前也要把解决方案配置切换为 `Release`；Debug 构建及其未优化的项目依赖会被 BenchmarkDotNet 拒绝，以免生成失真的性能结果。

| 基准类 | 覆盖内容 |
|---|---|
| `SpatialIndexBenchmarks` | 20,000 / 100,000 实体的 BVH 构建、分配式查询、复用缓冲区查询、1% 实体更新后查询 |
| `SelectionAvailabilityBenchmarks` | 512 / 20,000 个选中实体的按钮可用性全量检查与版本缓存读取 |
| `CommandHistoryBenchmarks` | 1,000 / 20,000 条历史的全栈快照复制与常数时间状态标记 |
| `SplineLengthBenchmarks` | 32 / 512 个拟合点的重复折线测长与缓存读取 |
| `CacheEvictionBenchmarks` | 128 / 1,024 个缓存候选项的排序淘汰与复用优先队列对照，检查耗时和托管分配；不创建 GPU 资源 |
| `SelectionOverlayBenchmarks` | 1 / 512 / 20,000 个选中实体的 handle/outline 构建，对比新集合、复用缓冲区、场景复用和版本化排序复用 |
| `OwnerBoundsUpdateBenchmarks` | 20,000 / 100,000 实体空间中，单个实体修改后的全量边界扫描与增量边界树更新 |
| `PreparationSnapshotBenchmarks` | 20,000 / 100,000 实体的后台准备快照，全量复制与修改页复制的耗时和分配对比 |
| `DirtyRegionBatchBenchmarks` | 512 / 20,000 个脏矩形的批量归并，保守覆盖原区域并限制输出数量 |
| `Direct2DSelectionOverlayBenchmarks` | 512 / 20,000 个选中实体实际进入 Direct2D selection overlay 的缓存回放和大选择集 fallback |
| `DirtyRegionBenchmarks` | 8 / 32 / 128 个脏矩形的批量规划和增量 Union |
| `Direct2DRenderingBenchmarks` | line-only / mixed 文档、LOD 开关、完整帧、单/多脏区域、缓存恢复、GPU 资源冷重建、单帧及连续 16 帧 pan/zoom、缩放快照预览 |
| `Direct2DResourceUpdateBenchmarks` | 单实体与 100 实体 geometry 变更时的 Direct2D 资源更新 |
| `ComplexSceneRenderingBenchmarks` | 5,000 个文字、2,000 个 hatch、2,000 个 Block Reference（每个展开 12 个实体）以及 512 个图像的热帧、资源重建和首帧 |
| `DocumentIoBenchmarks` | 生成文档或指定 `.d2cad` 的同步/异步读写、Section 读取、空间索引构建以及加载到 Direct2D 首帧的完整管线 |
| `LayoutRenderingBenchmarks` | Layout 纸空间、模型 Viewport、激活/未激活 Viewport 的热帧、资源重建和首帧 |
| `Direct2DParallelRenderingBenchmarks` | 20,000 mixed 实体，单线程/共享设备/多设备、2/4 worker 的完整缓存热帧 |
| `ParallelSubmissionBenchmarks` | 20,000 Line，1280/3840 宽、10%/100% 包围盒占屏，绕过场景缓存的提交/合成 |
| `StrokeQueryPaddingBenchmarks` | 20,000 Line 加远处宽线，屏幕/模型线宽对查询扩边和候选量的影响 |
| `BlockDependencyUpdateBenchmarks` | 20,000 引用，全量与受影响引用的边界更新 |
| `SelectionGeometryUpdateBenchmarks` | 20,000 选中实体，全量与单实体增量选择边界更新 |

后台计划准备的 `EntityPreparationSnapshots` 使用不可变分页，仅复制发生变化的页；只修改几何时复用原有绘制顺序。chunk / tile 在一次变更批次内去重失效，实体排序缓存按受影响的所属空间失效，文档结构变化仍保守地全量失效。实体换层时保留可复用的 geometry 和文字布局，并更新外观资源。编辑器创建实体会先确定目标空间再发布变更，Redo 也保持原目标空间。这些优化不改变绘制精度或 LOD 设置；快照复制基准不代表完整帧耗时。

搜索面板在连续变更版本下按实体更新结果，批量更新合并集合通知；多选属性跳过无关实体变更，并按属性类别刷新。后台准备复用成员数组和未变化的块依赖，chunk 计划按所属空间及引用关系失效。仅淘汰缩放缓存时可异步取消录制，修改共享资源前仍等待后台退出。保存先获取一致的 DTO 快照，再逐 section 序列化、写入临时文件并补写目录表，避免同时保留全部压缩载荷；文件格式和原子替换机制不变。

实现中细分了表级变更：图层改名、锁定和样式变化不再统一当作文档结构变化；图层顺序变化仍重绘场景，但不重建实体几何和空间索引。原生 geometry 的后台准备读取独立值快照，通过有界队列逐项交付；实体修改只废弃该实体的旧结果，其余任务继续。队列上限限制的是待接收结果，不是已使用资源的寿命。

撤销状态比较使用常数时间标记，不再复制或持有全部历史。`CommandHistorySettings.MaximumUndoCommands` 是可选的命令条数软上限，默认 `0` 不限制；只淘汰最旧的完整批次，最新批次即使超限也保留，Undo/Redo 模式仍在操作时读取。它不是精确字节预算。Spline 长度按几何变化失效；保存快照复用不可变的图片/OLE 数据，仍在文档所属线程采集其他可变状态，后台逐 section 写入。保存期间发生的新修改不会误标记为已保存。

`CadDocumentSaveSession` 统一处理每个文档的保存排队、取消、文件路径和修改状态基线。WPF 保存按 128 个实体分段采集快照，以约 4 ms 为让出 UI 的目标；每次恢复后校验编辑版本，变化时丢弃快照并最多重试两次。序列化仍只读取独立 DTO，取消或快照失败不会替换原文件。同步存储 API 保留一次性采集行为。

后台 geometry 消费同时受数量和约 2 ms 的时间预算约束，过期结果的释放也计入预算。Spline/Polyline 的 LOD geometry 在准备阶段基于值快照后台生成，普通绘制和选中绘制只读取资源；未就绪时使用完整 geometry。时间预算不抢占单个实体操作。首帧基准等待准备完成并验证 Present，统计值校验不替代像素对比测试。

`CadOleSessionController` 负责 OLE 会话、MessagePipe 更新订阅、命令化更新和释放；`Direct2DLayoutRenderer` 负责纸张、Layout 视口裁剪和绘制参数。两者位于现有项目，不增加新的程序集。

先列出所有基准：

```powershell
dotnet run -c Release --project .\Direct2dCad.Benchmarks\Direct2dCad.Benchmarks.csproj -- --list flat
```

快速 smoke 只验证基准能够初始化和完成，不应用于性能比较：

```powershell
dotnet run -c Release --project .\Direct2dCad.Benchmarks\Direct2dCad.Benchmarks.csproj -- --smoke --filter "*SpatialIndexBenchmarks*"
```

需要较快获得带预热和多次迭代的初步趋势时，可使用 BenchmarkDotNet 的 `ShortRun`；它比 smoke 可靠，但仍不替代完整基准：

```powershell
dotnet run -c Release --project .\Direct2dCad.Benchmarks\Direct2dCad.Benchmarks.csproj -- --job short --filter "*QueryVisibleArea*"
```

默认的 IO 基准使用可复现的 20,000 实体 mixed 文档。使用真实图纸时通过 `--document` 指定文件；该参数只作用于 `DocumentIoBenchmarks`：

```powershell
dotnet run -c Release --project .\Direct2dCad.Benchmarks\Direct2dCad.Benchmarks.csproj -- --document "C:\Drawings\large.d2cad" --filter "*DocumentIoBenchmarks*"
```

运行单组或完整基准：

```powershell
dotnet run -c Release --project .\Direct2dCad.Benchmarks\Direct2dCad.Benchmarks.csproj -- --filter "*Direct2DRenderingBenchmarks*"
dotnet run -c Release --project .\Direct2dCad.Benchmarks\Direct2dCad.Benchmarks.csproj
```

正式比较前应关闭调试器和其他高负载程序，并在相同机器、电源模式和构建版本下运行。同一工作区内不要并行启动多个基准进程，避免它们竞争 BenchmarkDotNet 的临时构建目录。报告同时输出 Mean、P95 和托管内存分配；CSV、HTML 和 GitHub Markdown 文件生成在 `BenchmarkDotNet.Artifacts/results`。

历史结果可以使用 `scripts/benchmarks/Compare-BenchmarkResults.ps1` 比较。脚本按方法、Category 和参数匹配场景，超过阈值时返回非零退出码，可直接用于 CI：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\benchmarks\Compare-BenchmarkResults.ps1 `
  -BaselineCsv .\benchmarks-baseline\SpatialIndex-report.csv `
  -CurrentCsv .\BenchmarkDotNet.Artifacts\results\Direct2dCad.Benchmarks.SpatialIndexBenchmarks-report.csv `
  -Metric P95 `
  -MaxRegressionPercent 10 `
  -FailOnMissing
```

首帧和 Direct2D 基准会创建真实 Windows 图形设备，应在具有稳定 GPU 驱动的 Windows x64 环境运行。OLE 的性能依赖外部 COM Server 和对象内容，不纳入默认基准；应针对固定 OLE 样本单独建立可选测试，避免默认基准因机器环境而失去可复现性。
