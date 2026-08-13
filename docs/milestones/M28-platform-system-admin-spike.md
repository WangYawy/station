# M28：平台系统管理（组织架构 / 用户权限 / 审计日志）

> 状态：✅ 通过（2026-08-13，Spike 9/9 + 前端构建 + M27 回归）
> 目标：补齐平台系统管理能力，组织、用户、角色、审计不再依赖种子数据手工维护；全部端点按 RBAC 权限码 + 部门树数据范围生效。

## 1. 交付内容

**后端**（新增 [SystemManagementController](../../src/Station.Platform/Station.Platform.Api/Controllers/SystemManagementController.cs)，均 `[Authorize]`）：

- 组织架构：`POST/PUT/DELETE /api/v1/depts`（`dept:manage`；编码唯一、上级部门校验、环检测、删除引用保护——有子部门/用户/采集站时拒绝）；`GET /depts` 增加数据范围过滤（非全量角色仅返回本部门及下级）；
- 用户管理：`GET /users`（`user:view` + 范围过滤，联查部门/角色/账号）、`POST /users`（`user:manage`，事务创建用户+账号+角色，默认密码 `Station@123`）、`PUT /users/{id}`（改部门/角色/状态）、`POST /users/{id}/reset-password`；
- 角色管理：`GET /roles`、`GET /permissions`、`POST /roles`（自定义角色，编码留空自动生成）、`PUT /roles/{id}`（**预置角色只读**，修改返回 400）；
- 审计日志：`GET /audit-logs`（`audit:view` + 范围过滤 + 关键字/时间区间），所有管理写操作自动落审计（`dept.create` / `user.create` / `role.update` / 重置密码等）。

**前端**（新增"系统管理"菜单，懒加载 chunk 18KB）：

- 四标签页：组织架构（el-tree 部门树 + 新增子部门/编辑/停用）、用户管理（增删改查 + 角色分配 + 重置密码）、角色管理（权限按模块分组勾选 + 数据范围）、审计日志（关键字/时间区间筛选）；
- 按钮按会话权限显隐（`dept:manage` / `user:manage` / `role:manage`）。

## 2. 验证结果（Spike 9/9）

| 场景 | 结果 |
|---|---|
| 未登录访问用户管理 → 401 | ✅ |
| 部门创建 TEAM2/GRP2 并出现在列表 | ✅ |
| 负责人只见本部门及下级（不含 TEAM2/ROOT），新建部门 403 | ✅ |
| 用户创建（事务：用户+账号+角色）→ 角色=操作员 | ✅ |
| 新用户可登录；操作员访问用户/部门管理 403 | ✅ |
| 用户改角色 + 重置密码后新密码可登录 | ✅ |
| 角色创建（2 权限）→ 编辑（3 权限）；预置角色修改 400 | ✅ |
| 删除有用户的部门 → 400（引用保护） | ✅ |
| 审计：dept.create / user.create 落库；负责人查不到跨部门审计（0 条） | ✅ |
| 前端构建通过；M27 资源托管回归 3/3 | ✅ |

验证代码：[spikes/Station.Spike.PlatformSystemAdmin](../../spikes/Station.Spike.PlatformSystemAdmin/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.PlatformSystemAdmin -c Release
```

## 3. 说明与后续（M29 建议）

1. 真实 UMS/MTP 采集源 + SFTP 真实服务器联调（采集侧闭环收尾，需真实设备）；
2. 记录仪管理页（全站台账 / 绑定 / 白名单）列 P1；
3. 统计/台账导出（CSV/Excel，按权限过滤并脱敏）列 P1；
4. 组织/用户 Excel 导入列 P1。
