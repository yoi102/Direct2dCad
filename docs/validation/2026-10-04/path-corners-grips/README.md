# 多段线拐角、圆打断与控制点数值输入验证

日期：2026-10-04。本机 Windows / .NET 10 Release 验证，改动尚未提交或发布。

## 结果

- Release WPF 构建：0 warning、0 error。
- 几何与命令：30/30，`TestResults/path-corners/commands-final/yoiri_YOIRI_2026-10-04_03_54_26_net10.0.trx`。
- ViewModel、控制点、绘图输入与 AI 工具：198/198，`TestResults/path-corners/viewmodels-verified/yoiri_YOIRI_2026-10-04_03_54_52_net10.0.trx`。
- 真实窗口：一次运行 3/3，`TestResults/path-corners/ui-foreground/yoiri_YOIRI_2026-10-04_03_52_11_net10.0.trx`。
- 最终构建的圆半径输入再验证：1/1，`TestResults/path-corners/ui-circle-final/yoiri_YOIRI_2026-10-04_03_59_20_net10.0.trx`；截图前等待 `R: 125` 与实际半径 125 的绿色圆弧同时显示。
- 三份 UI binding trace 均为空；资源键重复及差异检查、`git diff --check` 单独记录在 `verification.json`。

## 验证范围

几何检查覆盖开放/闭合多段线局部与全部拐角、首尾接缝、反向选择时两个倒角距离的归属、直线/圆弧混合路径、不同开放路径端部的保留与延伸、过大尺寸拒绝、预览不改源对象、类型转换、撤销/重做，以及圆跨 0° 打断后为单个真实 `CadArc`。

控制点检查覆盖直线长度/方向、中心坐标、圆半径、圆弧端点半径/角度、旋转椭圆的对应半轴、旋转矩形宽高、多段线顶点坐标、文档单位换算、非法输入拒绝、取消、一次撤销/重做、移出/返回画布的预览。已有控制点操作和绘图动态输入检查一同运行。AI 工具验证单路径局部/整体圆角倒角，以及原生保存重开后实体类型和几何保留。

UI 三项分别验证：

1. 同一闭合多段线相邻边圆角预览、确认、撤销；整条多段线倒角图标切换、参数输入、预览、确认、撤销。
2. 直线控制点 `Tab` 输入长度与方向、输入框不重叠、画布外仍有实际绿色几何、Enter 提交与一次撤销。
3. 圆控制点半径键盘输入与确认；无锁定绘图预览移出画布后保留；取消没有创建额外实体。

## 截图

- [多段线局部圆角](polyline-local-fillet-preview.png)
- [整条多段线倒角](polyline-all-chamfer-preview.png)
- [直线控制点数值](line-grip-numeric-preview.png)
- [控制点预览保留](line-grip-outside-canvas.png)
- [圆半径输入](circle-grip-radius-input.png)
- [绘图预览进入画布外之前](drawing-preview-before-leave.png)
- [绘图预览在画布外保留](drawing-preview-outside-canvas.png)

早期失败 TRX 保留在 `TestResults/path-corners`：无吸附时黄色位置标记属于独立覆盖层，取消后不能将其误判为残留实体；修改工具保存离开位置需用原始坐标，避免网格改变偏移侧；动态框仅在画布/框交互时出现；鼠标移向画布外会先改变最后有效几何位置。另有一次桌面运行受到其他 CAD 窗口遮挡，截图不可作为本应用证据。最后成功运行增加前台 HWND 检查，以实际绿色几何和当前应用截图验证，不计入受遮挡记录。

3/3 UI 运行后只补充了数值修正时清除旧控制点错误、相关模型用例与工具接口说明；最终模型检查、Release 构建与圆半径 UI 再验证包含这些调整。`verification.json` 的源码/程序 hash 是最终构建快照，不声称早期 UI 二进制与最终构建完全相同。

未执行完整产品回归、多 DPI/三种语言的逐屏视觉验收、性能测量、提交/推送、安装包或 GitHub 发布。
