# M11：站↔平台通信与断网补报（M1 契约落地）

> 状态：✅ 通过（2026-08-13，SQLite + Kingbase 5/5；平台 API 冒烟 200）
> 目标：把 M1 站↔平台通信契约落地：注册/配置同步/文件元数据/报警/指令轮询回执，断网本地队列补报。

## 1. 交付内容

### 领域层

- `SyncOutbox`（`station_sync_outbox`）：站→平台上报表（topic + JSON 载荷 + 状态/重试次数/下次重试时间），断网缓存补报载体。

### 应用层（Station.Application.PlatformSync）

| 组件 | 说明 |
|---|---|
| `PlatformOptions` | `Station:Platform`：BaseUrl/StationCode/同步间隔/指令轮询间隔（独立）/超时/Enabled |
| `IPlatformClient` / `HttpPlatformClient` | M1 契约 HTTP 客户端：注册、元数据、报警、配置同步、指令轮询、回执（`ApiResponse<T>` 统一包装） |
| `ISyncOutboxService` | 入队 + 消费补报；失败保留（`RetryCount`/`NextRetryAt` 指数退避 15s→300s） |
| `ICommandService` / `ICommandExecutor` | 指令轮询→执行（P0 六类）→回执入队上报；重启/清缓存/重拉配置/自检/停止/启动 |

### 桌面端

- `PlatformSyncWorkerHostedService`：注册（进程内一次）→ 补报 Outbox → 指令轮询（独立间隔）执行回执；`Enabled` 默认关（单机版不连平台）。

### 平台端（Station.Platform.Api）

- `InMemoryPlatformStore` + `StationCommunicationController`：6 个契约端点（内存存储，三库持久化 M12+），冒烟注册返回 `{"success":true,...,"stationId":2}`。

## 2. 验证结果（SQLite + Kingbase × 5 项）

| 验证项 | SQLite | Kingbase |
|---|---|---|
| 注册上报（Outbox→平台收到） | ✅ | ✅ |
| 文件元数据 + 报警上报 | ✅ | ✅ |
| 指令轮询→执行→回执上报 | ✅ | ✅ |
| 断网补报（失败入队→恢复→补报成功，重试计数+1） | ✅ | ✅ |
| 清理 | ✅ | ✅ |

验证代码：[spikes/Station.Spike.PlatformSync](../../spikes/Station.Spike.PlatformSync/Program.cs)（内嵌 FakePlatform HttpListener 模拟平台）

```powershell
dotnet run --project spikes/Station.Spike.PlatformSync -c Release -- --db sqlite
dotnet run --project spikes/Station.Spike.PlatformSync -c Release -- --db kingbase
```

平台 API 冒烟：

```powershell
dotnet run --project src/Station.Platform/Station.Platform.Api -c Release --urls http://127.0.0.1:5100
curl -X POST http://127.0.0.1:5100/api/v1/stations/register -H "Content-Type: application/json" --data-binary @archive/platform-register.json
```

## 3. 设计说明

1. **本地队列=入库表**：Outbox 是断网补报载体（用户问题确认：本地队列即入库表），成功标 Sent 留痕，失败指数退避重试；
2. **指令独立轮询间隔**：`CommandPollIntervalSeconds` 与上报间隔分开（需求：独立的轮询间隔）；
3. **回执经 Outbox**：指令回执先入队再上报，断网回执不丢；
4. **平台内存存储**：M11 只验证契约通路；平台三库（MySQL/PG/Kingbase）持久化与采集站注册/配置应用 M12 接入。

## 4. 配置

```json
{
  "Station": {
    "Platform": {
      "Enabled": false,
      "BaseUrl": "http://localhost:5100",
      "StationCode": "ST0001",
      "SyncIntervalSeconds": 10,
      "CommandPollIntervalSeconds": 10,
      "TimeoutSeconds": 30
    }
  }
}
```

## 5. 后续（M12 建议）

- 平台三库持久化（Station.Platform 接入 Station.Infrastructure）+ 采集站台账/文件统一列表；
- 采集完成自动入队元数据上报（现为契约通路，业务挂接 M12）；
- 配置同步应用（平台下发 → 本地配置落库/生效）；
- 真实 UMS/MTP 采集源 + SFTP 真实服务器联调收尾。
