# M4 Spike：数据访问层落地（Station.Infrastructure）

> 状态：✅ 通过（2026-08-12）
> 目标：按 M3 结论落地共享数据访问层：SqlSugar 工厂、IDbDialect 方言抽象、通用仓储、UnitOfWork、CodeFirst 初始化、配置化 DI，并在四库上验证全链路。

## 1. 交付内容

新增共享项目 `src/Station.Shared/Station.Infrastructure`：

| 组件 | 文件 | 职责 |
|---|---|---|
| `DbProvider` / `DbOptions` | `Db/DbProvider.cs`、`Db/DbOptions.cs` | 四库枚举 + 配置节 `Station:Db` |
| `IDbDialect` | `Db/Dialects.cs` | 收敛版本/索引/表目录方言 SQL（四库四实现 + 工厂） |
| `ISqlSugarFactory` | `SqlSugarFactory.cs` | SqlSugarClient（事务隔离）/ SqlSugarScope（线程安全共享）创建 |
| `IRepository<T>` / `RepositoryBase<T>` | `Repositories/` | 通用 CRUD + 分页（`PageResult<T>`），复杂查询经 `AsQueryable()` 组合 |
| `IUnitOfWork` / `UnitOfWork` | `UnitOfWork/` | 独立客户端 + 独立事务；`UseTran` / `UseTranAsync`，Dispose 自动回滚 |
| `IDatabaseInitializer` | `DatabaseInitializer.cs` | CodeFirst 建表、版本/索引/表目录诊断 |
| `AddStationDatabase` | `DependencyInjection.cs` | 配置绑定 + DI 注册（Scope 单例、UoW 独立客户端） |

接入：`Station.Desktop.Infrastructure.AddInfrastructure` 调用 `AddStationDatabase(configuration)`；新增 `appsettings.json` 配置模板（SQLite 默认）。

## 2. 验证结果（4 库 × 7 项 = 28 项全 PASS）

| 验证项 | Kingbase | MySQL | PostgreSQL | SQLite |
|---|---|---|---|---|
| 工厂+方言+初始化器 连接/版本 | ✅ | ✅ | ✅ | ✅ |
| CodeFirst 建表（CodeFirst） | ✅ | ✅ | ✅ | ✅ |
| 仓储：批量插入 100 + 分页 | ✅ | ✅ | ✅ | ✅ |
| UnitOfWork：回滚/提交 | ✅ | ✅ | ✅ | ✅ |
| 方言：索引目录 | ✅ | ✅ | ✅ | ✅ |
| DI + 配置绑定（AddStationDatabase） | ✅ | ✅ | ✅ | ✅ |
| 清理 | ✅ | ✅ | ✅ | ✅ |

验证代码：[spikes/Station.Spike.DataAccess](../../spikes/Station.Spike.DataAccess/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.DataAccess -c Release -- --db kingbase
dotnet run --project spikes/Station.Spike.DataAccess -c Release -- --db mysql
dotnet run --project spikes/Station.Spike.DataAccess -c Release -- --db postgresql
dotnet run --project spikes/Station.Spike.DataAccess -c Release -- --db sqlite
```

## 3. 设计说明与约定

1. **配置切换**：`Station:Db:Provider`（Sqlite/Kingbase/MySql/PostgreSQL）+ `Station:Db:ConnectionString`，采集站默认 SQLite，切换金仓只改配置。
2. **线程模型**：常规读写注册 `SqlSugarScope` 单例（线程安全）；`IUnitOfWork` 每次创建独立 SqlSugarClient 保证事务隔离，避免共享连接上的事务互相干扰。
3. **命名规范**：实体用 `[SugarTable]`/`[SugarColumn]` 显式映射（小写下划线），`InitKeyType.Attribute`；Kingbase/PostgreSQL 统一 `IsAutoToUpper=false`，与 M3 结论一致。
4. **方言隔离**：业务代码不得直接拼 `version()`/`pg_indexes` 等方言 SQL，统一走 `IDbDialect`。
5. **事务用法**：`uow.UseTranAsync(...)` 或 `Begin/Commit/Rollback`；未显式结束的 UoW 在 Dispose 时自动回滚。
6. **CodeFirst**：`EnsureCreated(typeof(...))` 建表；手写迁移脚本按库分目录（`mysql/`、`pgsql/`、`kingbase/`、`sqlite/`），M5 业务表落地时补充。

## 4. 遗留说明

- 领域实体（Account/User/Dept/Recorder/VideoFile）暂未加 SugarTable 映射属性，待 M5 首个业务模块设计表结构时统一补齐（避免先行定义后返工）。
- 平台端 `Station.Platform.*` 尚未创建自己的 Infrastructure 项目，可直接引用共享 `Station.Infrastructure`（三库驱动已内置），接入点与桌面端一致。

## 5. 结论

数据访问层四库通用能力已验证，桌面端 DI 已接线，配置切换即用。M5 可开始首个业务模块（建议：账号认证/用户与部门，或采集任务与文件台账）。
