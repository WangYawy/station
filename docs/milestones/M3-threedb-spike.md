# M3 Spike：三库双端适配验证（MySQL / PostgreSQL / Kingbase + SQLite）

> 状态：✅ 通过（2026-08-12）
> 目标：清零技术选型 R-01"三库双端适配"风险。平台三库（MySQL/PostgreSQL/Kingbase）与采集站本地双库（SQLite/Kingbase）统一走 SqlSugar 抽象层，验证 连接/建表/批量插入/分页/事务/索引 全链路。

## 1. 环境

| 数据库 | 版本 | 运行位置 | 端口 | 驱动（NuGet） |
|---|---|---|---|---|
| KingbaseES | V8R6C9B14（ORACLE 模式） | WSL2 Ubuntu | 54321 | Kdbndp 8.0.0 |
| MySQL | 8.0.46 | WSL2 Ubuntu | 3306 | MySqlConnector 2.3.7 |
| PostgreSQL | 16.14（PGDG） | WSL2 Ubuntu | 5432 | Npgsql 8.0.9 |
| SQLite | 3.49.1 | 本地文件（Microsoft.Data.Sqlite） | — | Microsoft.Data.Sqlite 10.0.9 |

ORM：SqlSugarCore 5.1.4.216（`DbType.Kdbndp / MySql / PostgreSQL / Sqlite`）

验证代码：[spikes/Station.Spike.DatabaseAdapter](../../spikes/Station.Spike.DatabaseAdapter/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.DatabaseAdapter -c Release -- --db kingbase
dotnet run --project spikes/Station.Spike.DatabaseAdapter -c Release -- --db mysql
dotnet run --project spikes/Station.Spike.DatabaseAdapter -c Release -- --db postgresql
dotnet run --project spikes/Station.Spike.DatabaseAdapter -c Release -- --db sqlite
```

## 2. 验证结果矩阵

| 验证项 | Kingbase | MySQL | PostgreSQL | SQLite |
|---|---|---|---|---|
| SqlSugar 连接 + 版本 | ✅ | ✅ | ✅ | ✅ |
| CodeFirst 建表（7 列） | ✅ | ✅ | ✅ | ✅ |
| 批量插入 100 条 | ✅ | ✅ | ✅ | ✅ |
| 条件查询 + 分页（34 条 / 第 2 页） | ✅ | ✅ | ✅ | ✅ |
| 事务回滚 / 提交 | ✅ | ✅ | ✅ | ✅ |
| 索引（2 业务索引 + 主键） | ✅ | ✅ | ✅ | ✅ |
| 清理测试表 | ✅ | ✅ | ✅ | ✅ |

四库 28 项全部通过，耗时均 < 4s。

## 3. 关键发现（必须写入数据访问层规范）

1. **Microsoft.Data.Sqlite 版本修正**：SqlSugarCore 5.1.4.216 直接依赖 `Microsoft.Data.Sqlite >= 10.0.9`，技术选型中"8.0.x"会导致 NU1605 包降级错误。已同步修正 [02 技术选型文档](../02视音频数据采集站与监控管理平台 — 开发基线技术选型方案.md) 版本锁定清单为 **10.0.x（随 SqlSugarCore 依赖锁定）**。
2. **PostgreSQL 版本**：Ubuntu 22.04 官方源默认为 PG 14，技术选型要求 15+，需添加 PGDG 源安装 16（本机 16.14）。安装方式见 [03 环境手册](03开发环境搭建 — WSL2与KingbaseES V8R6 安装运维手册.md) 的 MySQL/PG 补充章节。
3. **库相关 SQL 差异需抽象**（SqlSugar 已收敛大部分，但以下两类要封装）：
   - 版本查询：Kingbase/PG/MySQL 用 `select version()`，SQLite 用 `select sqlite_version()`；
   - 索引目录：Kingbase/PG 用 `pg_indexes`，MySQL 用 `information_schema.statistics`，SQLite 用 `pragma_index_list`。
4. **连接串差异**（配置模板需按库维护）：
   - Kingbase：`Host=localhost;Port=54321;Database=test;Username=system;Password=***`
   - MySQL：`Server=localhost;Port=3306;Database=station_spike;Uid=station;Pwd=***;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None`
   - PostgreSQL：`Host=localhost;Port=5432;Database=station_spike;Username=station;Password=***`
   - SQLite：`Data Source=<路径>`
5. **大小写行为**：Kingbase ORACLE 模式 + `CASE_SENSITIVE=YES` 与 PostgreSQL 在 SqlSugar 下均按引号标识符原样存储；MySQL 与 SQLite 大小写不敏感。实体/表名统一小写下划线即可四库通用。
6. **WSL 服务生命周期**：MySQL/PostgreSQL/Kingbase 服务随 WSL VM 生命周期存亡，空闲约 8 秒 VM 关机；测试/开发期间用 `wsl -e sleep infinity` 保活（见环境手册）。
7. **管理入口差异**：Ubuntu 内 MySQL root 走 auth_socket（`sudo mysql`），PostgreSQL 管理走 `su postgres -c psql`；应用账号（station）按 4 中连接串授权。

## 4. 对数据访问层（Infrastructure）的落地建议

- 统一 `ISugarUnitOfWork`/仓储基类，`DbType` 与连接串由配置切换（`Station:Db:Provider` + `Station:Db:ConnectionString`）。
- 建表/迁移：优先 SqlSugar CodeFirst；若需手写 SQL，按三库分目录维护（`mysql/`、`pgsql/`、`kingbase/`、`sqlite/`）。
- 通用查询（版本、索引、统计）抽成 `IDbDialect` 小接口，避免业务代码散落方言 SQL。
- 事务、分页、批量插入经本次验证四库行为一致，直接走 SqlSugar API 即可。

## 5. 结论

**三库双端适配风险（R-01）已清零**。SqlSugar 5.1.4.216 + 四款官方驱动在 建表/批量写入/分页/事务/索引 上与产品需求完全兼容；数据访问层按第 4 节建议落地即可进入业务模块开发（M4+）。
