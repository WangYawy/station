# M5 Spike：账号认证 + RBAC + 数据权限（Station.Application）

> 状态：✅ 通过（2026-08-12，四库 10/10）
> 目标：落地首个业务模块"账号认证与 RBAC"：登录（失败锁定/改密）、统一 RBAC（四预置角色+自定义角色+权限点）、组织树数据隔离、登录审计。

## 1. 交付内容

### 领域层（Station.Domain，新增 SugarTable 映射）

| 实体 | 表名 | 说明 |
|---|---|---|
| `Account` | `station_account` | 登录账号（SM3 密码哈希、失败次数、锁定时间） |
| `User` | `station_user` | 人员（工号唯一、归属部门） |
| `Dept` | `station_dept` | 部门/组织树（编码唯一、ParentId 层级） |
| `Role` | `station_role` | 角色（编码唯一、DataScope、IsSystem） |
| `Permission` | `station_permission` | 权限点（`module:action`） |
| `RolePermission` | `station_role_permission` | 角色-权限关联 |
| `UserRole` | `station_user_role` | 用户-角色关联 |
| `AuditLog` | `station_audit_log` | 审计日志（登录等关键操作） |

### 基础设施层（Station.Infrastructure）

- `IIdGenerator` / `SnowflakeIdGenerator`：雪花 ID，跨四库通用（主键显式赋值，替代数据库自增）
- `IPasswordHasher` / `Sm3PasswordHasher`：**PBKDF2-HMAC-SM3**（国密合规），格式 `sm3$迭代$盐$哈希`
- `AuthSeeder`：幂等种子（15 个权限点 + 4 预置角色 + 根部门 + 系统管理员账号）

### 应用层（Station.Application）

- `IAuthenticationService`：登录/登出/改密；**连续失败 5 次锁定 15 分钟**（可配置 `Station:Auth`）
- `IAuthorizationService`：会话解析（角色/权限/数据范围）、权限判定（admin 角色全通过）
- `IDataScopeProvider`：组织树数据隔离（All / 本部门及下级 / 仅本部门 / 仅本人）
- `IUserService`：部门/用户/账号/角色 CRUD、角色分配、权限点维护（事务内走 UnitOfWork 仓储）
- `IAuditLogService`：登录等关键操作审计

## 2. 验证结果（4 库 × 10 项 = 40 项全 PASS）

| 验证项 | SQLite | Kingbase | MySQL | PostgreSQL |
|---|---|---|---|---|
| 种子数据（4 角色/15 权限/管理员） | ✅ | ✅ | ✅ | ✅ |
| SM3 密码哈希（验证/拒绝/格式） | ✅ | ✅ | ✅ | ✅ |
| 登录失败锁定（5 次→锁定，锁定期拒登） | ✅ | ✅ | ✅ | ✅ |
| 管理员登录（admin 角色、全权限、All 范围） | ✅ | ✅ | ✅ | ✅ |
| 部门树+用户+账号+角色分配 | ✅ | ✅ | ✅ | ✅ |
| 数据范围（操作员=本人/负责人=本部门及下级/审计员=All） | ✅ | ✅ | ✅ | ✅ |
| 权限判定（file:view 有、user:manage 无 等） | ✅ | ✅ | ✅ | ✅ |
| 登录审计（成功+失败均留痕） | ✅ | ✅ | ✅ | ✅ |
| 修改密码（旧密错拒/改密/新密登录/还原） | ✅ | ✅ | ✅ | ✅ |
| 清理 | ✅ | ✅ | ✅ | ✅ |

验证代码：[spikes/Station.Spike.Auth](../../spikes/Station.Spike.Auth/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.Auth -c Release -- --db sqlite
dotnet run --project spikes/Station.Spike.Auth -c Release -- --db kingbase
```

## 3. 关键发现与约定（重要）

1. **金仓表名禁止 `sys_` 前缀**：Kingbase ORACLE 兼容模式保留 `sys` 系统命名空间，`sys_user` 等表名会与系统对象冲突（`42809: "sys_user" is not a table or materialized view`）。**业务表统一 `station_` 前缀**。
2. **SQLite 自增主键限制**：SqlSugar CodeFirst 对 `long` + `IsIdentity` 在 SQLite 上生成非法 DDL（`AUTOINCREMENT is only allowed on an INTEGER PRIMARY KEY`）。**主键统一雪花 ID 显式赋值**（`IIdGenerator`），四库行为一致，且契合"本地文件 ID 跨端引用"。
3. **可空列必须显式 `[SugarColumn(IsNullable = true)]`**：本配置下 SqlSugar 不会按 `Nullable<T>`/`string?` 自动建 NULL 列，漏标会导致 NOT NULL 约束失败。
4. **事务内必须使用 UnitOfWork 的仓储**：`IUnitOfWork` 持有独立客户端；事务内若混用共享作用域仓储，SQLite 双连接写锁会死锁（实测卡死）。规则：**Begin 后所有读写都走 `uow.GetRepository<T>()`**。
5. **登录锁定语义**：第 5 次失败即返回 `LockedOut` 并写入 `LockedUntil`；锁定期内即使密码正确也拒绝。
6. **角色数据范围取最宽**：用户多角色时取 `DataScope` 最小值（All=0 最宽）；admin 角色对权限判定直接放行。
7. 领域实体使用显式默认值替代 `required`（SqlSugar `Insertable<T>` 需要 `new()` 约束）。

## 4. 落地路径（M6 建议）

- 桌面端 UI：登录窗口、会话保持（1 分钟无操作自动退出）、导航按权限点显隐；
- 单机版内置 Web：登录 API + Cookie 认证 + RBAC 中间件（复用本里程碑服务）；
- 免登录模式配置与"操作人"确定（记录仪绑定/手选）；
- 采集任务/文件台账按 `IDataScopeProvider` 结果过滤。
