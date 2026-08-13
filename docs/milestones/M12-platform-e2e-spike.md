# M12：采集→台账→平台元数据上报 端到端 + 平台三库持久化

> 状态：✅ 通过（2026-08-13，端到端 5/5：站端 SQLite/Kingbase + 平台 MySQL 落库）
> 目标：把 M11 的通信通路接到采集业务（采集完成 → SM3/FileNo/归属台账 → 元数据上报），平台端由内存存储升级为三库持久化。

## 1. 交付内容

### 领域层

- `CollectFile` 增加 `Sm3`（采集后本地缓存 SM3）与 `FileNo`（`{采集站编号}-{本地文件ID}`）；
- `CollectTask` 增加 `LedgeredAt`（台账处理标记，幂等）；
- `VideoFile`（文件台账）补齐 SugarTable 映射（`station_video_file`，FileNo 唯一）；
- 新增平台专属领域项目 `Station.Platform.Domain`：`PlatformStation` / `PlatformFileMetadata` / `PlatformAlertReport` / `PlatformCommand`（平台三库建表）。

### 基础设施

- `Sm3Checksum`：SM3 文件/字符串校验（"采集即校验"元数据，国密）。

### 应用层

- `FileKindMapper`：扩展名 → 契约 `FileKind`（Video/Audio/Image/Other）；
- `IStationContext`：注册成功后回填平台 StationId（供元数据上报）；
- `IFileLedgerService.ProcessCompletedTaskAsync`：为已完成任务写文件台账（SM3/FileNo/归属/时间），并按需入 Outbox 上报 `file-metadata`（断网补报）；
- `LedgerAndReportWorkerHostedService`（桌面端）：扫描"采集完成未入台账"任务自动处理。

### 平台端（Station.Platform.Api）

- 存储从内存升级为 **SqlSugar 三库持久化**（MySQL/PostgreSQL/Kingbase 配置切换，默认 MySQL）；
- 控制器落库：注册（幂等）、元数据（按 StationId+LocalFileId 去重）、报警、指令轮询（Pending→Pulled）、回执（状态/结果回写）；
- 启动时 `EnsureCreated` 平台四表；连接串见 `appsettings.json`（`station_platform` 库）。

## 2. 验证结果（端到端）

| 步骤 | 结果 |
|---|---|
| 平台注册（真实 API → MySQL `platform_station`） | ✅ StationId 雪花回传 |
| 采集完成（模拟记录仪 3/3） | ✅ |
| 台账 + SM3（64 位）+ FileNo（`ST-E2E-...`）+ 归属 | ✅ 3 条 |
| 元数据上报（Outbox 3 条 → 平台 API → MySQL `platform_file_metadata` 3 条） | ✅ |
| 清理（站端表；平台库测试表清理） | ✅ |

站端 SQLite 与 Kingbase 均通过；平台侧 MySQL 实测落库，PostgreSQL/Kingbase 通过 `Station:Db:Provider` 切换。

验证代码：[spikes/Station.Spike.PlatformE2E](../../spikes/Station.Spike.PlatformE2E/Program.cs)（内嵌启动真实平台 API）

```powershell
dotnet run --project spikes/Station.Spike.PlatformE2E -c Release -- --db sqlite
dotnet run --project spikes/Station.Spike.PlatformE2E -c Release -- --db kingbase
```

## 3. 平台联调

```powershell
dotnet run --project src/Station.Platform/Station.Platform.Api -c Release
# 桌面端 appsettings: Station:Platform.Enabled=true, BaseUrl=http://127.0.0.1:5100
```

## 4. 后续（M13 建议）

- 平台文件统一列表/检索/预览（`platform_file_metadata` 数据已就绪）；
- 真实 UMS/MTP 采集源接入与 SFTP 真实服务器联调；
- 配置同步应用（平台下发 → 本地生效）；
- 平台报警列表/处置与采集站端对接。
