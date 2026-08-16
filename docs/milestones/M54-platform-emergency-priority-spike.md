# M54：平台版紧急优先（上报/展示/配额下发）

> 状态：✅ 通过（2026-08-16，Station.Spike.StationDetail 12/12 + DesktopAudit 17/17 + 双端全量构建 + linux-x64 发布）
> 目标：把 M53 的紧急优先扩展到平台版——采集站上报紧急优先任务快照，平台详情页展示，配额（上限）可随采集策略集中下发。

## 1. 采集站 → 平台上报

- 契约：`EmergencyTaskReport/EmergencyTaskItem`（任务号/记录仪/协议/进度/开始时间），路由 `POST /stations/{stationId}/emergency-tasks`（站通道，与既有上报一致）；
- 采集站 `PlatformSyncWorkerHostedService` 每个同步周期上报当前紧急任务快照（来自 `CollectTaskService.GetActiveTasksAsync` 中 `IsEmergency` 的任务，进度与工作台卡片同口径）；
- 上报失败静默、下轮重试（快照式幂等，无需补报队列）。

## 2. 平台存储与实时

- 新增 `PlatformEmergencyTask`（含站归属 DeptId 数据权限快照），平台启动 CodeFirst 建表；
- `StationCommunicationController.ReportEmergencyTasks`：整表替换该站快照（空列表=清空），刷新心跳，广播 `emergency.updated`（SignalR）→ 平台详情页实时刷新。

## 3. 平台 UI（采集站详情）

- 设备信息 Tab 新增"🚨 紧急优先任务"卡片：任务号/记录仪/协议/进度条/开始时间，空态提示"由操作员在采集站工作台卡片上标记"；
- 策略配置 Tab 新增"紧急优先上限"输入（0-30），随 CollectPolicy 下发（`ConfigApplyService.ApplyCollectPolicy` 应用 `maxEmergencyTasks`，采集站热生效并回显到设置模块）。

## 4. 验证（Station.Spike.StationDetail 12/12）

| 场景 | 结果 |
|---|---|
| 紧急任务上报后详情返回 2 条（含进度/开始时间） | ✅ |
| 空快照上报清零 | ✅ |
| 策略下发含 maxEmergencyTasks 并可在配置记录查询 | ✅ |
| 原有详情聚合/记录仪过滤/配置与指令鉴权/趋势回归 | ✅ |
| DesktopAudit 17/17（本地紧急上限）回归 + linux-x64 发布 | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.StationDetail -c Release
dotnet run --project spikes/Station.Spike.DesktopAudit -c Release
```

## 5. 后续

- 平台远程"标记/取消紧急优先"指令（平台→采集站→回执闭环）暂未实现；当前紧急优先由采集站操作员在卡片上操作，平台侧只读展示与配额下发。
