# M15：配置同步真正应用（平台下发 → 采集站热更新生效 → 增量拉取）

> 状态：✅ 通过（2026-08-13，Spike 6/6）
> 目标：把 M14 的配置拉取升级为"真正应用"：平台发布采集/存储策略变更，采集站热更新即时生效，已应用版本增量拉取不重复，全程审计。

## 1. 交付内容

### 平台端

- `PlatformConfigChange`（`platform_config_change`）：按站发布的配置变更（EntityType/Operation/PayloadJson/递增 Version）；
- `PlatformConfigsController`：`POST/GET /api/v1/stations/{id}/configs`（发布/查询）；
- `config-sync` 端点改造：按采集站上报的 `AppliedVersions` 增量返回未应用变更。

### 采集站

- `IConfigApplyService`：按 EntityType 应用：
  - `CollectPolicy`：自动采集开关 / 采集后擦除 / 跳过已采集 → **热更新** `CollectOptions` 单例；
  - `StoragePolicy`：存储目标 / 目录模板 / 重试次数 / 熔断阈值 → 热更新 `StorageOptions` 单例；
  - 其他类型记录"暂不支持"跳过；每次应用写审计（`config-apply`）；
- `IConfigSyncState` 扩展：按 EntityType 记录已应用版本 + 最近同步时间；
- 关键改造：采集/存储/上传服务改为**直接注入配置单例**（`CollectOptions`/`StorageOptions`），使热更新即时生效（不再用只读 Options 快照）；
- `PlatformSyncWorker`：请求携带 AppliedVersions → 拉取 → 应用 → 标记同步。

## 2. 验证结果（Spike 6/6）

| 步骤 | 结果 |
|---|---|
| 平台发布 采集策略 + 存储策略 两条变更 | ✅ |
| 采集站热更新生效（AutoCollect=false/Erase=true/Retry=5/熔断=8 + 已应用版本） | ✅ |
| 热更新即时生效（自动任务停在 Created 未自动开始） | ✅ |
| 增量拉取不重复（再拉变更数 0） | ✅ |
| 审计留痕（config-apply 2 条） | ✅ |
| 清理 | ✅ |

验证代码：[spikes/Station.Spike.ConfigSync](../../spikes/Station.Spike.ConfigSync/Program.cs)（迷你桌面端宿主 + 真实平台 API + MySQL）

```powershell
dotnet run --project spikes/Station.Spike.ConfigSync -c Release
```

## 3. 联调入口

```powershell
dotnet run --project src/Station.Platform/Station.Platform.Api -c Release
# POST http://127.0.0.1:5100/api/v1/stations/{id}/configs
# {"entityType":"CollectPolicy","operation":0,"payloadJson":"{\"autoCollectOnConnect\":false,\"eraseAfterComplete\":true}"}
```

## 4. 说明与后续（M16 建议）

1. 支持配置类型可扩展（OperationAuth/授权等按相同模式注册）；
2. 配置版本落库持久化（当前进程内；重启后重新全量拉取，平台按 AppliedVersions 返回——重启后 AppliedVersions 空会重复应用，幂等即可，落库为优化项）；
3. 真实 UMS/MTP 采集源 + SFTP 真实服务器联调收尾；
4. 平台前端 Vue 工程化。
