# M21：平台登录认证 + RBAC 报警处置（401/200/403 闭环）

> 状态：✅ 通过（2026-08-13，Spike 4/4）
> 目标：平台管理操作纳入统一 RBAC：共享账号体系登录（Cookie）、查询/处置需认证、处置需 `alert:handle` 权限。

## 1. 交付内容

### 平台端（Station.Platform.Api）

- 接入共享认证：`AddStationApplication` + Cookie Authentication（`station_platform_auth`）+ Authorization；
- 平台库种子认证表（Account/User/Dept/Role/Permission/... + `AuthSeeder` 预置角色与 admin 账号）；
- `AuthController`：`POST /api/v1/auth/login`（共享 `AuthenticationService`，登录写 Cookie）、`logout`、`me`；
- 查询端点（alerts/stations/files）加 `[Authorize]`：未登录 401；
- **报警处置**：`POST /api/v1/alerts/{id}/status`（确认/处理/关闭）——需认证 + `alert:handle` 权限（无权限 403）。

### 前端

- 登录区（账号/密码/登录/退出 + 当前用户角色）；
- 报警表格加状态列 + 确认/处理/关闭按钮（403 提示无权限）。

## 2. 验证结果（Spike 4/4）

| 步骤 | 结果 |
|---|---|
| 未登录访问报警查询 → 401 | ✅ |
| admin 登录（Cookie）→ 查询报警 2 条 | ✅ |
| admin 处置报警 → 200 + 状态更新 | ✅ |
| 审计员（无 alert:handle）处置 → 403 | ✅ |

验证代码：[spikes/Station.Spike.PlatformRbac](../../spikes/Station.Spike.PlatformRbac/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.PlatformRbac -c Release
```

## 3. 联调入口

```powershell
dotnet run --project src/Station.Platform/Station.Platform.Api -c Release
# 浏览器 http://127.0.0.1:5100/ → admin / Admin@123 登录 → 报警管理处置
```

## 4. 说明与后续（M22 建议）

1. **组织数据权限**（上级看全部下级、同级隔离）为 P1：需平台组织树与采集站/报警的部门归属（`PlatformStation.DeptId` 上报）落地后按 `DataScopeProvider` 过滤；
2. 平台操作审计（登录/处置/配置发布写 `AuditLog`）可复用共享审计服务；
3. 真实 UMS/MTP 采集源、SFTP 真实服务器联调、平台前端 Vue 工程化仍待办。
