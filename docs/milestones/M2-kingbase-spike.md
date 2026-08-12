# M2 Spike：KingbaseES（人大金仓）三库适配验证

> 状态：✅ 通过（2026-08-12）
> 目标：验证采集站/平台对 Kingbase 的 P0 适配链路（技术选型 R-01 风险项），驱动与 ORM 组合为 **Kdbndp 8.0.0 + SqlSugarCore 5.1.4.216**。

## 1. 环境

| 项 | 值 |
|---|---|
| 数据库 | KingbaseES V008R006C009B0014（V8R6C9B14） |
| 运行位置 | WSL2（Ubuntu 22.04.1 LTS，内核 5.10.16.3） |
| 宿主 | Windows 11 企业版 22631（本机） |
| 安装方式 | ISO 控制台安装，完全安装集，`/opt/Kingbase/ES/V8` |
| 兼容模式 | ORACLE（`CASE_SENSITIVE=YES`，块 8k，UTF8） |
| 端口/账号 | 54321 / system |
| License | 官网"开发版"授权文件（安装 SERVER 组件必须提供，否则服务无法启动） |
| .NET | net8.0，Kdbndp 8.0.0（NuGet 官方包）+ SqlSugarCore 5.1.4.216 |
| 连接串 | `Host=localhost;Port=54321;Database=test;Username=system;Password=***`（Windows 通过 WSL2 localhost 转发直连） |

## 2. 验证项与结果

| # | 验证项 | 结果 |
|---|---|---|
| 1 | Kdbndp 原生驱动直连 + `select version()` | ✅ |
| 2 | SqlSugar `DbType.Kdbndp` 连接 | ✅ |
| 3 | CodeFirst 自动建表（7 列，含主键） | ✅ |
| 4 | 批量插入 100 条 | ✅ |
| 5 | 条件查询 + 分页（第 2 页 10 条，总数 34） | ✅ |
| 6 | 事务：回滚后计数不变 / 提交后 +1 | ✅ |
| 7 | 索引：`idx_spike_status`、`idx_spike_collected` + 主键索引均生效 | ✅ |
| 8 | 清理测试表 | ✅ |

验证代码：[spikes/Station.Spike.Kingbase](../../spikes/Station.Spike.Kingbase/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.Kingbase -c Release
```

## 3. 关键差异点与注意事项（开发期必须遵守）

1. **SqlSugar 枚举是 `DbType.Kdbndp`，不是 `DbType.Kingbase`**（5.1.4.216 实测）。
2. **Kingbase 必须提供 license 文件**：安装集含 SERVER 时 `KB_LICENSE_PATH` 必填；无 license 时安装"看似成功"（exit 0）但服务起不来。开发版 license 从金仓官网下载中心获取。
3. **大小写敏感**：ORACLE 模式 + `CASE_SENSITIVE=YES` 时，SqlSugar 生成的带引号标识符按原样存储，表名/列名大小写行为与 Oracle 一致；实体与建表 SQL 需统一风格（本项目统一小写下划线）。
4. **首次 `DropTable` 会抛 42P01**：SqlSugar 的 `DbMaintenance.DropTable` 在 Kingbase 上不做 IF EXISTS 兜底，调用前需 try/catch 或先 `IsAnyTable` 判断。
5. **分页/事务/索引** 与 PG 语义兼容（limit/offset、ACID、pg_indexes 目录视图），SqlSugar 抽象层可直接收敛差异。
6. **WSL2 空闲自动关机**：约 8 秒无活动会话后 VM 关闭，ISO 挂载、数据库服务随之停止。开发期间保持一个 `wsl` 终端常驻，或用 `wsl -e sleep infinity` 保活；正式交付不依赖 WSL。
7. **语言环境**：Ubuntu 默认只有 `C` locale，安装器选 `zh_CN.UTF-8` 会报 "Locale not supported"；需先 `locale-gen zh_CN.UTF-8`，或直接选 `C`（编码仍为 UTF8）。
8. **数据目录权限**：安装目录 `/opt/Kingbase/ES/V8` 需 `sudo chown -R kingbase:kingbase /opt/Kingbase` 后才能由 kingbase 用户写入。

## 4. 结论

Kdbndp + SqlSugarCore 对 Kingbase V8R6（ORACLE 兼容模式）的建表、批量写入、分页、事务、索引能力验证通过，**P0 金仓适配风险解除**。数据访问层按技术选型统一走 SqlSugar 抽象即可，后续 MySQL/PostgreSQL 复用同一套 Spike 逻辑（仅换 `DbType` 与连接串）继续验证。
