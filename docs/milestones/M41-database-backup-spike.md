# M41：数据备份（本地库每日 7 份 + 平台每日 30 份/手动 + 审计）

> 状态：✅ 通过（2026-08-13，Spike 4/4 + ComposeValidate 5/5）
> 目标：需求 10.8 数据备份规则——采集站本地库每日备份保留 7 份；平台库每日备份保留份数可配（默认 30）并支持手动备份；备份动作留审计。

## 1. 交付内容

- [DatabaseBackupService](../../src/Station.Shared/Station.Infrastructure/Backup/DatabaseBackupService.cs)（`Station:Backup`）：
  - **SQLite**：`Sqlite Backup API` 一致性快照（`Pooling=False` 避免连接池锁文件），还原 = 替换库文件；
  - **MySQL/PostgreSQL/Kingbase**：逻辑 SQL 导出（跨库、容器友好、不依赖 mysqldump/pg_dump），还原**按表先清空再插入（幂等）**；
  - 保留份数裁剪（`RetentionCount`），文件名毫秒级时间戳防同秒覆盖；
- **桌面端**：`LocalBackupWorkerHostedService` 每日 03:00 自动备份（默认保留 7 份），备份动作写审计；
- **平台端**：`PlatformBackupWorker` 每日 03:00 自动备份（默认保留 30 份）+ `BackupsController`（`GET /api/v1/backups` 列表、`POST` 手动备份，仅管理员），自动/手动均留审计；
- **前端**：平台"系统管理"新增"数据备份"页（手动备份 + 备份列表）；
- **部署**：三个 Docker Compose 增加 `backups` 命名卷与 `STATION__BACKUP__*` 配置，`.env` 支持 `BACKUP_RETENTION`。

## 2. 验证结果（Spike 4/4）

| 场景 | 结果 |
|---|---|
| SQLite 备份文件生成（直读行数一致）→ 还原后行数一致 | ✅ |
| 备份保留裁剪：连续备份后仅保留配置份数（3/3） | ✅ |
| MySQL 逻辑导出（含 INSERT）→ 幂等还原后行数一致 | ✅ |
| 平台手动备份端点：200 + 备份文件生成 + 审计 `backup.manual` | ✅ |
| ComposeValidate 回归（新增备份卷后）5/5 | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.Backup -c Release
```

## 3. 说明与后续

- 还原流程：目标环境先由平台启动自动建表，再用备份文件执行还原（SQLite 直接替换库文件）；文档中注明；
- 备份目录建议挂独立磁盘/对象存储（平台容器已挂 `backups` 卷）；
- 剩余 P0 缺口：定时采集、任务暂停/恢复、缓存保留天数清理、远端 SM3 二次校验、时钟回拨检测、桌面端报警声音弹窗、状态监控页。
