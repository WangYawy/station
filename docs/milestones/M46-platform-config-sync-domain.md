# M46：用户域配置全量下发（组织/用户/账号/角色/记录仪白名单）

> 状态：✅ 通过（2026-08-13，Spike 8/8）
> 目标：P1 增强——需求 7.3"配置同步"完整化：组织、用户、账号、角色、记录仪白名单从平台增量下发到采集站本地；断网时本地缓存继续支撑采集与登录（平台模式桌面端/单机 Web 登录均走本地库）。

## 1. 交付内容

### 契约（`Station.Contracts/Sync/ConfigDomainDtos.cs`）

- `DeptSyncRow / UserSyncRow / AccountSyncRow / RoleSyncRow / UserRoleSyncRow / RecorderSyncRow`：用户域快照行 DTO，**全部以自然键跨库传递**（`Code / UserNo / UserName / SerialNumber`），平台雪花主键不落采集站本地，本地按自然键 upsert 并重建外键，规避跨库主键冲突；
- `ConfigDomainPayload`：实体类型常量（`Dept/User/Role/UserRole/Account/Recorder`）与平台发布依赖顺序（先部门后用户/角色，再账号/记录仪）。

### 平台侧（`PlatformConfigsController.PublishDomain`）

- `POST /api/v1/stations/{stationId}/configs/sync-domain`（需登录 + `user:manage`）：从平台库读取组织树/用户/账号/角色/角色权限/记录仪台账（白名单或已绑定），构建全量快照，按依赖顺序写入 6 条 `platform_config_change`（每条独立递增版本）；
- 记录仪行仅下发 `IsActive && (IsWhitelisted || BoundUserNo != null)`，白名单字段直接透传为本地 `station_recorder.IsAuthorized`；
- 落审计 `config.publish-domain`（含各部门/用户/角色/账号/记录仪数量）。

### 采集站侧（`ConfigApplyService` 扩展）

- `Dept`：按 `Code` upsert，父级按 `ParentCode` 两遍回填（避免行序问题）；
- `User`：按 `UserNo` upsert，`DeptId` 按 `DeptCode` 映射本地部门（缺失回落 ROOT）；
- `Role`：按 `Code` upsert，角色权限按权限点编码全量重建（幂等）；
- `UserRole`：派生表，先清空再按快照重建；
- `Account`：按 `UserName` upsert，`PasswordHash`（`sm3$迭代$盐`）跨库直接透传，`UserId` 按 `UserNo` 映射；本地运行态 `FailedLoginAttempts/LockedUntil` 不被快照覆盖；
- `Recorder`：按 `SerialNumber` upsert，`BoundUserId/DeptId` 按自然键映射本地外键；
- 快照外缺失行一律**软停用**（`IsActive=false`/`IsEnabled=false`）而非物理删除，保留历史文件归属引用；单条应用失败不阻塞后续，记失败审计、不记已应用版本，下轮轮询自动重试；
- 应用统一使用 `ISqlSugarFactory.CreateClient(autoCloseConnection:false)` 独立长连接，规避共享作用域与采集/查询并发竞争（沿用 M42 规范）。

## 2. 验证结果（Spike 8/8）

| 场景 | 结果 |
|---|---|
| 采集站注册（ST-DOM 上报平台） | ✅ |
| 未授权调用 sync-domain → 401 | ✅ |
| 发布 6 类用户域快照（Dept/User/Role/UserRole/Account/Recorder） | ✅ |
| 轮询拉取应用：`AppliedVersions` 全部记录（v1–v6） | ✅ |
| 本地库断言：部门树 ROOT→TEAM1→GRP1 本地外键重建、用户挂本地部门、账号哈希可直接验密、角色权限关联 38 条、用户角色绑定、R-DOM-001 白名单+绑定本地用户/部门 | ✅ |
| 断网本地登录：`zhangsan/Test@123` 本地库验证成功且带 operator 角色 | ✅ |
| 白名单增量同步：平台将 R-DOM-002 加白后二次发布，Recorder 版本 6→12，本地自动更新 | ✅ |
| 平台审计留痕：`config.publish-domain` 2 条 | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.ConfigDomainSync -c Release
```

## 3. 说明与后续

- 同步语义为"全量快照、以平台为准"：采集站本地为平台用户域只读镜像；平台模式登录（桌面端/单机 Web）天然走本地库，断网自动可用；
- 与现有 `CollectPolicy/StoragePolicy` 热更新并存：策略类仍是 options 热更，用户域走本地库落盘，两者共用同一版本/轮询通道（`Station:Platform:SyncIntervalSeconds` 默认 10 秒，生产可按需调大）；
- 待办提醒：平台发布操作目前需管理员在平台手动触发（或由后台任务定时发布）；白名单变更走既有 `recorder.whitelist` 接口后需再发布一次；
- 剩余 P1：xlsx/PDF 导出、SignalR 实时推送、MTP 采集源。
