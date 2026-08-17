# M55：多设备/混合协议采集（UMS + MTP 按设备路由）

> 状态：✅ 通过（2026-08-17，DesktopAudit 17/17、Collect 7/7、UmsSource 6/6、MtpSource 9/9 + 双端构建 + linux-x64 发布）
> 目标：一台采集站同时接入多个设备（全 UMS / 全 MTP / UMS+MTP 混合）时，按每台设备的实际协议选择对应的连接与读取方式，且多 UMS 盘按设备根路径路由，不再只取第一个盘。

## 1. 按设备协议路由采集源

- 新增 `ICollectSourceProvider.GetFor(ProtocolType)`：
  - 共享默认实现（`DefaultCollectSourceProvider`）：模拟源或 UMS；
  - 桌面端 `CollectSourceProvider`：真实模式下 UMS/私有SDK(转U盘) → UmsCollectSource，MTP → MtpCollectSource（非 Windows 抛明确错误）；
- `CollectTaskService` 由单一 `ICollectSource` 改为按任务协议解析采集源：扫描/复制/擦除均用该任务对应设备的采集源；
- DI：共享层注册各采集源与默认 Provider，桌面端注册 MTP 感知 Provider 覆盖（HostBuilderFactory 应用服务先、基础设施后的顺序已保证覆盖生效）。

## 2. 多 UMS 盘按设备根路径路由

- `CollectDeviceInfo` 增加 `RootPath`；`CollectTask` 增加 `SourceRoot`（存量库 `EnsureColumn` 补列）；
- 接入监听（工作台/自动采集）为每台设备携带根路径：UMS=盘符根、MTP=`MTP://PnP`；
- `UmsCollectSource` 扫描/复制/擦除优先使用 `device.RootPath`，无根路径时回退原"枚举第一个可移动盘"逻辑（兼容旧任务）；
- `ICollectSource.CopyAsync` 增加 device 参数，MTP 仍以文件内编码的 `PnP\u001F对象ID` 定位。

## 3. 接入监听混合识别

- `RecorderConnectMonitor` 真实模式（SourceMode ≠ simulated）下同时运行 UMS 与 MTP 检测器并合并去重，不再按单一 SourceMode 选择一种协议；
- `RecorderConnectWatcherHostedService` 对每台设备按其协议识别（ini/台账）并创建任务（携带 RootPath），未绑定/篡改/非授权照常写报警。

## 4. 验证

| 场景 | 结果 |
|---|---|
| 混合协议同时识别（UMS 盘 + MTP 设备同轮触发，按各自协议处理） | ✅（DesktopAudit） |
| 全流程采集（自动/跳过/中断/暂停恢复/取消/擦除）经 Provider 路由 | ✅（Collect 7/7） |
| UMS 复制带设备根路径（CopyAsync 新签名） | ✅（UmsSource 6/6） |
| MTP 采集源回归 | ✅（MtpSource 9/9） |
| 桌面/平台全量构建 + linux-x64 单文件发布 | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.DesktopAudit -c Release
dotnet run --project spikes/Station.Spike.Collect -c Release
dotnet run --project spikes/Station.Spike.UmsSource -c Release
```

## 5. 说明

- `CollectOptions.SourceMode` 语义更新：`ums`/`mtp` 均表示"真实设备模式"，接入的设备按实际协议逐台处理（混合无需再区分配置）；
- 顺带修复 Collect spike 缺失 ClockState/LicenseInfo 表导致的过期失败（M16 后 license 检查引入的库表未同步到 spike）。
