# 批量更新与并行绘制优化

日期：2026-09-05。基于当前工作区，保留已有修改；不修改文件格式或用户设置默认值。

## 实现

- `ExecuteAtomicBatch` 内的命令仍独立进入历史。空间索引和块引用边界立即更新，资源更新、文档通知和操作日志在批次结束时合并；原子调用失败后按最终实体生命周期发送恢复通知，保留之前的 redo 分支。
- `BlocksToolboxViewModel` 按块目录版本刷新，复用 `BlockItemViewModel`，重命名只调整行位置。普通实体编辑或选择变化不再清空列表，面板关闭期间延迟刷新。
- 并行入口复用普通绘制的空间查询、有序候选集及可见性判断，不再无条件扫描整个空间。
- 两种并行模式均保留 worker 设备、context 和实体缓存。只为实际分配到的实体准备资源，变更时更新已有缓存；窗口调整大小只替换 `Direct2DWorkerRenderTarget`。删除释放对应资源，设备丢失仍重建 worker 池。
- 工作分配复用现有实体绘制成本估算，包括 Spline、Polyline 和 Hatch，保持连续绘制顺序，不按类型重新排序。
- 完整有效的 tile / command list 缓存优先于并行逐实体提交。并行开关表示允许使用该优化，不再强制绕开更便宜的缓存路径；原有 LOD 和近似 tile 缩放设置仍参与缓存匹配。

## 性能验证

运行 `Direct2DParallelRenderingBenchmarks.FullFrameWarmCache`，20,000 个 Mixed 实体，1600 x 900，Release，BenchmarkDotNet Short，2 次预热、3 次测量。两次运行顺序执行，没有同时运行测试套件。

比较的是本轮“完整缓存优先”修正前后，其余早先的本轮优化已经包含在第一次测量中，不能视作整个工作区修改前后的比较。

| 配置 | 缓存优先修正前 Mean | 修正后 Mean |
| --- | ---: | ---: |
| 关闭并行 | 101.0 us | 105.45 us |
| 多设备，2 worker | 17,386.3 us | 103.58 us |
| 多设备，4 worker | 12,902.3 us | 95.09 us |
| 共享设备，2 context | 34,111.8 us | 103.37 us |
| 共享设备，4 context | 43,503.1 us | 113.34 us |

修正后各配置均使用已经完成的缓存，托管分配约 1.77 KB/操作。差异主要是消除了开启并行后的缓存旁路退化，不是 GPU 逐实体绘制提速数百倍。

这些数字是离屏 `Render` 调用的预热场景耗时，**不是屏幕 FPS，也不是等待 GPU 完全执行后的时间**。样本次数较少，不能根据 95 与 113 us 的差异判断设备模式优劣。冷启动、持续缩放、大 Hatch、实际图纸及不同驱动还需单独采样。

原始报告：

- `TestResults/performance-optimization/parallel/results/`
- `TestResults/performance-optimization/cache-first/results/`

复现：

```powershell
dotnet run --project Direct2dCad.Benchmarks/Direct2dCad.Benchmarks.csproj -c Release -- --filter '*Direct2DParallelRenderingBenchmarks*' --job Short --warmupCount 2 --iterationCount 3
```

## 回归重点

最终 Release 解决方案构建为 0 警告、0 错误。13 个测试项目共 1298 / 1298 通过，其中托管测试 1176 个，Windows/Direct2D 集成测试 122 个。本轮新增 13 个用例，并强化已有 resize 用例的缓存复用断言。覆盖率汇总器自检和 `git diff --check` 通过。

本轮没有重跑 UI 自动化，没有重新采集覆盖率百分比。完整 TRX 位于 `TestResults/performance-optimization/regression/`。

- 批量执行只通知一次，批次内部空间查询仍能看到已执行的命令。
- 单步与整批 undo/redo；失败创建、失败删除后资源与历史恢复。
- 块列表项和选中项身份保持；重命名重排；关闭、重新打开面板。
- 可见性使用缓冲区查询并恢复绘制顺序；复杂度分配不遗漏或重复实体。
- 两种并行模式仅准备可见子集；调整尺寸不更换 worker 池；移动、撤回后的像素一致；删除后资源数量减少。
- 完整缓存优先且不创建不必要的 worker 资源；设备丢失、透明度、绘制顺序等已有集成回归继续执行。

本轮没有改变 worker 的全画布纹理布局，没有增加自动遮挡剔除，也没有把可变文档直接交给异步渲染线程。资源会在实体删除或设备重置时释放；已访问实体的缓存可继续保留，不在每帧全量清扫。
