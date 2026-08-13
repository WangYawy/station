# M48：平台 SignalR 实时推送 + MTP 采集源

> 状态：✅ 通过（2026-08-13，RealtimePush Spike 3/3 + MtpSource Spike 9/9）
> 目标：平台端事件实时到达（注册/文件上报/报警/指令回执/授权状态等），桌面端补齐 MTP 记录仪采集（扫描/复制/擦除/绑定文件 WPD 存取）。

## 1. SignalR 实时推送（平台 → Web）

### 服务端（`Station.Platform.Api`）

- `Realtime/RealtimePush.cs`：
  - `StationHub`：`/hubs/stations` 端点，同源 + Cookie 认证，`withAutomaticReconnect` 自动重连；
  - `IRealtimeEventBus / RealtimeEventBus`：业务写入点发布 `StationRealtimeEvent(Type, StationId, DeptId, OccurredAt)`，Hub 全量广播；
  - 事件类型常量：`station.registered / file.reported / alert.created / command.result / station.license / station.status / recorder.whitelist`。
- `StationCommunicationController` 六处写入点接入总线：注册（新/重复）、文件上报、报警上报、授权状态上报、指令回执上报；
- `Program.cs`：`AddSignalR()` + `MapHub<StationHub>("/hubs/stations")`。

### 前端（`station-platform-web`）

- `utils/realtime.ts`：SignalR 连接单例，`onRealtimeEvent(type, handler)` 按事件类型分发，页面卸载自动退订；
- `AdminLayout` 挂载即连接、卸载断开；六个页面订阅对应事件后自动刷新：文件（file.reported）、报警（alert.created）、指令（command.result）、记录仪（recorder.whitelist）、采集站（registered/status/license）、统计（file.reported/alert.created/license）。

### 事件载荷设计

广播只带"事件类型 + 归属（StationId/DeptId）+ 时间"，不含业务明细——客户端收到后按自身数据权限重新拉取列表，避免越权数据经 Hub 泄漏，也避免事件明细与库表结构耦合。

### 验证（[Station.Spike.RealtimePush](../../spikes/Station.Spike.RealtimePush/Program.cs) 3/3）

| 场景 | 结果 |
|---|---|
| 两个 Web 客户端同时收到 `station.registered` 广播（含 stationId） | ✅ |
| 报警上报后 `alert.created` 推送到客户端 | ✅ |
| 授权状态上报后 `station.license` 推送 | ✅ |

## 2. MTP 采集源（桌面端，Windows）

### 采集源（[MtpCollectSource](../../src/Station.Desktop/Station.Desktop.Infrastructure/Collecting/MtpCollectSource.cs)）

- 通过 Windows 便携设备 API（WPD，Vanara.PInvoke.PortableDeviceApi 5.0.6）实现 `ICollectSource`：
  - **扫描**：`DEVICE` 根递归枚举对象，跳过文件夹/功能对象，取文件名（`WPD_OBJECT_ORIGINAL_FILE_NAME`）、大小（`PKEY_GenericObj_ObjectSize`）、修改时间；文件路径编码为 `{PnP设备ID}\u001F{对象ID}` 以支持复制/续采定位；
  - **复制**：`IPortableDeviceResources.GetStream`（`WPD_RESOURCE_DEFAULT`）分块读取到本地缓存，带进度回调与取消；
  - **擦除**：递归枚举后按扩展名白名单逐个 `Delete(WITH_RECURSION)`（普通删除，失败由既有擦除重试/报警流程处理，不阻塞任务）；
  - **设备解析**：`PortableDeviceManager` 枚举，支持 `MtpDeviceFilter`（友好名称/PnP ID 包含串）过滤，无设备抛出明确错误"未检测到 MTP 设备"；
  - MTP 无盘符，根目录为虚拟路径 `MTP://{PnP设备ID}`。

### 绑定文件 WPD 存取

- `IRecorderRootFileStore`（共享层）抽象根目录文件读写，`FileSystemRecorderRootFileStore` 保持 UMS/模拟源行为；
- `MtpCollectSource` 同时实现 `IRecorderRootFileStore`：绑定 ini 的读（流读取）、写（`CreateObjectWithPropertiesAndData` + IStream Commit）、删（对象删除）全走 WPD；设备不支持创建文件时给出明确错误；
- `CompositeRecorderRootFileStore`（桌面端）：按根目录前缀分发 `MTP://` → WPD，其余 → 文件系统；
- `RecorderBindingFile` 改为经 store 读写（内容解析/签名逻辑不变），`WriteBinding/Unbind`（单机 Web/桌面）与 `IdentifyAsync` 自动适配 MTP 虚拟根。

### 配置与 DI

- `CollectOptions.SourceMode` 新增 `mtp`（仅 Windows），`MtpDeviceFilter` 可配置；
- 桌面 DI 按模式选源（`mtp`/`ums`/`simulated`），非 Windows 选择 `mtp` 时抛 `PlatformNotSupportedException`；
- `HostBuilderFactory` 调整注册顺序（应用服务先、基础设施后），使桌面采集源与根文件存取覆盖共享层默认注册；
- 定时采集按 `SourceMode` 映射协议（mtp→MTP，其余→UMS）。

### 验证（[Station.Spike.MtpSource](../../spikes/Station.Spike.MtpSource/Program.cs) 9/9）

| 场景 | 结果 |
|---|---|
| SourceKey=mtp / 虚拟根目录 `MTP://` | ✅ |
| 无设备：GetRecorderRoot / Scan / 绑定读取均明确报错"未检测到 MTP 设备" | ✅ |
| DI `SourceMode=mtp` → `MtpCollectSource`；根文件存取 → `CompositeRecorderRootFileStore` | ✅ |
| 普通路径（UMS/模拟源）绑定文件读写删走文件系统，读写回环一致 | ✅ |
| 非 Windows 平台给出"仅支持 Windows"明确错误（代码路径 + 运行时守卫） | ✅ |

## 3. 真机接入说明

1. 记录仪 USB 连接并选择 **MTP 模式**（非 U 盘模式），Windows 可见"便携设备"；
2. 配置 `Station:Collect:SourceMode=mtp`（可选 `MtpDeviceFilter` 指定设备，如记录仪型号/厂商名）；
3. 采集任务自动按 WPD 扫描→缓存→（平台版）上报，任务完成后按策略擦除；绑定 ini 读写走 WPD（部分记录仪限制 MTP 下创建文件时，界面会给出"设备不支持写入绑定文件"提示，此时建议改用 UMS 模式绑定）。

## 4. 后续

- 真机联调：插入 MTP 记录仪后按上面步骤验证全链路（扫描/复制/擦除/绑定）；
- 私有加密设备（SDK 转 UMS）由 UMS 源覆盖（M33 已交付）；MTP 采集源不涉及设备侧 SDK。
