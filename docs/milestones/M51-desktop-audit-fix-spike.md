# M51：桌面端功能完善与跨平台审计修复

> 状态：✅ 通过（2026-08-15，Station.Spike.DesktopAudit 12/12 + win-x64/linux-x64 单文件发布）
> 目标：按上轮桌面端审计结论，按优先级补齐五项：数据目录统一、设备热插拔自动采集、工作台真实统计、历史/日志模块、Avalonia 过时告警清理。

## 1. 统一应用数据目录（P1，部署关键）

- 新增 `StationPaths`（共享层）：
  - Windows `%LOCALAPPDATA%\Station`，Linux `~/.local/share/Station`；`STATION__DATA__DIR` 可覆盖（测试/便携）；
  - `RebaseSqliteConnectionString`：SQLite 相对连接串（`Data Source=station.db`）重定位到数据目录，绝对路径/`:memory:`/`file:` URI 原样保留；
- `AddStationDatabase`：SQLite 连接串自动重定位 + 建目录（修复打包安装到 Program Files/`/usr/lib` 后非管理员无法建库的问题）；
- `RuntimeSettingsFile` 默认落数据目录（系统设置修改不再因安装目录只读而失败）；
- Linux `.deb` 桌面入口补 `Path=/usr/lib/station-desktop`，应用菜单启动即读到安装目录 conffile（appsettings.json），运行期可写数据仍在用户数据目录；
- 部署手册同步：04-1/04-2 的数据库/运行时设置/备份路径更新为数据目录。

## 2. 记录仪接入监听（P2，接入即自动采集闭环）

- `IRecorderDeviceDetector`：`UmsDeviceDetector`（可移动磁盘；`UmsRootOverride` 视为固定设备便于开发测试）与 `MtpDeviceDetector`（Windows WPD，非 Windows 恒空）；
- `RecorderConnectMonitor`：设备连续稳定 N 次轮询（默认 3 次 ≈ 9 秒，等挂载稳定）视为接入，去重处理；拔出后清除跟踪，再次插入可重新触发；模拟源/协议不匹配不工作；
- `RecorderConnectWatcherHostedService`（后台服务，3 秒轮询）：接入稳定 → `IdentifyAsync`（ini/台账识别，未绑定/篡改/非授权自动写报警）→ 已绑定且"接入自动采集"开启时 `CreateTaskAsync(isAuto:true)` 自动采集，启动失败写报警；
- 桌面 DI 注册检测器与后台服务；与 M49 设置页"接入自动采集"开关热联动。

## 3. 工作台真实统计（P3）

- 移除演示常量：今日采集改为本地库聚合（今日已完成文件数/容量），"本机运行"显示授权状态（LicenseService），设备连接池改为活跃任务 + 实际检测到的 UMS/MTP 设备（无设备时显示占位）；待上传、系统监控保持真实。

## 4. 桌面端历史记录 + 日志中心（P4）

- 历史记录：最近 200 条采集任务 + 选中任务的文件明细（`ICollectTaskService.GetTasksAsync/GetTaskFilesAsync`）；
- 日志中心：最近 200 条审计/操作日志（`IAuditLogService.GetRecentAsync`），含时间/操作人/类型/对象/详情/结果；
- 两者接入 ShellWindow 导航（原占位页），权限沿用 OperationAccessService（history→file:view，logs→audit:view）。

## 5. Avalonia 过时告警清理（P5）

- `TextBox.Watermark` → `PlaceholderText`；`Window.SystemDecorations` → `WindowDecorations`。

## 6. 验证（Station.Spike.DesktopAudit 12/12 + 发布）

| 场景 | 结果 |
|---|---|
| 数据目录环境变量生效 / SQLite 相对路径重定位 / 绝对路径与内存库不变 | ✅ |
| 运行时设置文件落数据目录 | ✅ |
| 宿主启动后 DB 落在数据目录（原 cwd 不再产生库文件） | ✅ |
| 模拟源不触发 / 协议不匹配不触发 / 稳定期内不提前触发 | ✅ |
| 连续 3 次稳定触发一次；在线不重复；拔出重插再触发 | ✅ |
| 全解决方案构建 + linux-x64/win-x64 单文件发布 | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.DesktopAudit -c Release
```

## 7. 遗留说明

- MTP 采集源与 MTP 检测仍仅支持 Windows（信创 Linux 接 MTP 记录仪需 libmtp 实现，属后续）；
- 工作台"本机运行"授权状态 30 秒刷新一次（授权检查含时钟回拨落库，避免高频写库）。
