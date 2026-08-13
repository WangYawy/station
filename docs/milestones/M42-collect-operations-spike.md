# M42：采集运维能力（定时采集 / 暂停恢复 / 缓存清理 / 远端SM3 / 时钟回拨）

> 状态：✅ 通过（2026-08-13，Spike 5/5 + M8/M37/M40 回归）
> 目标：补齐需求 7.1 剩余采集能力——后台定时开始、任务暂停/恢复（服务层已有，本次验证）、缓存保留天数清理、上传后远端 SM3 二次校验、授权时钟回拨检测。

## 1. 交付内容

- **定时采集**：`ScheduledCollectWorkerHostedService` 每日指定时间（`Station:Collect:ScheduleHour/Minute`，默认 02:00，开关 `ScheduledCollectEnabled`）自动创建采集任务；
- **暂停/恢复**：服务层（M7 已有 `PauseAsync/ResumeAsync/CancelAsync`）本次 spike 验证：暂停后任务置 `Paused`、当前文件回退待采集，恢复后继续完成；
- **缓存保留天数清理**：`CacheCleanupService` + 每日清理 Worker（默认 04:00），`CacheRetentionDays` 默认 30 天，清理动作留审计；
- **远端 SM3 二次校验**：`IStorageTarget.ComputeRemoteSm3Async`（本地/FTP/SFTP 三目标实现），上传后按 `StorageOptions.VerifyRemoteSm3`（默认开）比对远端与本地 SM3，不一致按失败重试；
- **时钟回拨检测**：`ClockState` 单行表记录最近授权检查时间，`LicenseService.CheckAsync` 检测到时间回拨（容差 2 分钟）返回锁定；
- **SQLite 并发修复**：启用 WAL + busy_timeout；共享 `SqlSugarScope` 改为常驻连接（auto-close=false），消除异步查询中 "reader is closed" 偶发（M8 回归亦更稳定）。

## 2. 验证结果（Spike 5/5 + 回归）

| 场景 | 结果 |
|---|---|
| 定时采集：到点自动创建任务并完成 | ✅ |
| 暂停/恢复：暂停→Paused，恢复→Completed | ✅ |
| 缓存保留天数清理：超期文件删除 | ✅ |
| 远端 SM3：本地目标与 SFTP 目标二次校验一致 | ✅ |
| 时钟回拨：LastCheckAt 超前 → Locked，恢复后正常 | ✅ |
| 回归：M8 上传 3/3、M40 缓存加密 4/4、M37 单机 Web 6/6 | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.Operations -c Release
```

## 3. 说明与后续

- 定时采集需要记录仪已连接（UMS 枚举可移动磁盘）；无设备时由采集流程报警，下轮重试；
- 远端 SM3 校验会完整读取远端文件（P0 可接受，可配置关闭）；
- 剩余 P0 缺口：桌面端报警声音弹窗、状态监控页（7.1 剩余 UI）。
