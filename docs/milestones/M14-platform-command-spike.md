# M14：平台指令下发→采集站执行→回执 + 停止/恢复采集 + 配置同步拉取

> 状态：✅ 通过（2026-08-13，Spike 7/7）
> 目标：打通平台→采集站的反向管控链路：下发指令、采集站独立轮询执行并回执；停止/启动采集真实生效；配置同步拉取记录。

## 1. 交付内容

### 平台端（Station.Platform.Api）

- `PlatformCommandsController`：
  - `POST /api/v1/stations/{id}/commands`：下发 P0 六类指令（创建 Pending，SM2 签名 P2）；
  - `GET /api/v1/stations/{id}/commands`：指令列表（含类型/状态/回执消息）；
- 前端 `wwwroot/index.html` 增加"远程指令管理"区块：采集站 ID + 指令类型下拉 + 下发 + 指令列表刷新。

### 采集站

- `ICollectControl`（进程内采集开关）：`StopCollecting` / `StartCollecting` 真实生效——`CollectTaskService.StartAsync` 在关闭状态下拒绝新任务；
- `CommandExecutor` 真实化：
  - 清缓存：删除本地缓存目录并统计；
  - 自检：数据库连通（`select 1`）+ 缓存目录可写；
  - 停止/启动采集：调用 `ICollectControl`；
  - 重启服务（宿主自动重启 P2）、重拉配置（同步 Worker 下一轮应用）为明确占位；
- `IConfigSyncState`：同步 Worker 每轮调用 `SyncConfigAsync` 并记录版本/变更数/时间（配置真正应用 P2）。

## 2. 验证结果（Spike 7/7）

| 步骤 | 结果 |
|---|---|
| 平台注册（StationId 回传） | ✅ |
| 平台下发 清缓存/自检/停止采集 三类指令 | ✅ |
| 采集站独立轮询执行 → 回执上报（3 条 Succeeded，消息真实） | ✅ |
| 停止采集后新任务被拒（"采集已停止"） | ✅ |
| 下发启动采集 → 恢复后任务 Completed | ✅ |
| 配置同步拉取（版本/变更数/时间记录） | ✅ |
| 清理 | ✅ |

验证代码：[spikes/Station.Spike.PlatformCommand](../../spikes/Station.Spike.PlatformCommand/Program.cs)（迷你桌面端宿主 + 真实平台 API + MySQL）

```powershell
dotnet run --project spikes/Station.Spike.PlatformCommand -c Release
```

## 3. 联调入口

```powershell
dotnet run --project src/Station.Platform/Station.Platform.Api -c Release
# 浏览器 http://127.0.0.1:5100/ → 远程指令管理：填采集站ID+选类型 → 下发 → 列表看回执
```

## 4. 说明与后续（M15 建议）

1. 指令签名（SM2）与验签、超时失败人工重发为后续补强；
2. 配置同步已拉取记录，真正"配置项应用"（采集策略/存储策略热更新）M15 落地；
3. 真实 UMS/MTP 采集源与 SFTP 真实服务器联调收尾；
4. 平台前端 Vue 工程化（station-platform-web）。
