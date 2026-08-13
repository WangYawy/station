# M43：桌面端报警声音/弹窗 + 状态监控页

> 状态：✅ 通过（2026-08-13，SystemMonitor Spike 2/2 + 桌面端全量构建通过）
> 目标：补齐需求 7.1 剩余 UI——新报警声音提示 + 弹窗；工作台状态监控（CPU/内存/磁盘/网络/端口/设备/采集进度）。

## 1. 交付内容

**报警声音 + 弹窗**

- [DesktopAlertSound](../../src/Station.Desktop/Station.Desktop.UI/Services/DesktopAlertSound.cs)：Windows 用控制台蜂鸣；Linux 生成 16-bit PCM 提示音 WAV 并尽力用 `paplay`/`aplay` 播放（不可用时静默降级）；
- [AlertPopupWindow](../../src/Station.Desktop/Station.Desktop.UI/Views/AlertPopupWindow.axaml)：右下角置顶无边框弹窗，展示报警标题/级别/类型，6 秒自动关闭；
- `ShellWindow` 新报警检测：轮询到**新增**待处理报警（按 ID 增量）时播放声音 + 弹窗，同时保留顶部报警横幅；

**状态监控（工作台）**

- [SystemMonitorService](../../src/Station.Desktop/Station.Desktop.Application/Monitoring/SystemMonitorService.cs)：CPU（Windows 性能计数器 / Linux `/proc/stat` 差值）、内存（可用/总量）、磁盘（已用/总量）、网络（上行/下行 KB/s）、监听端口、设备（UMS 数量/模拟源），跨平台降级 "—"；
- 工作台新增"状态监控"面板，每秒刷新队列、每 2 秒刷新监控数据；本机状态卡片（SystemText）同步显示 CPU/内存/磁盘实时值；
- 底部状态栏本机 IP 等静态示例后续接入（本里程碑覆盖核心指标）。

## 2. 验证结果

| 场景 | 结果 |
|---|---|
| 状态监控快照：CPU/内存/磁盘/端口/设备 6 项 | ✅ |
| 二次采样：CPU 与网络出现实时数值（Windows 实测 CPU 6%、可用 4GB/16GB、磁盘 398GB/803GB、端口列表） | ✅ |
| 桌面端全量解决方案构建通过（含新弹窗/声音/监控） | ✅ |
| 桌面端实例重启加载新 UI | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.SystemMonitor -c Release
```

## 3. 说明与后续

- 报警声音无法在无音频环境自动验证，代码走"尽力播放 + 降级"；真机验收时确认；
- 剩余基线差异清单 P0 已全部清零；后续为 P1 增强（维修/报废状态、记录仪/采集站导入、文件归属修正、白名单全量下发等）与真机/客户环境验收项。
