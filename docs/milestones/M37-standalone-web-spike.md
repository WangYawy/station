# M37：单机版内置 Web（登录 / RBAC 数据隔离 / 文件查询 / 管理 / 审计导出）

> 状态：✅ 通过（2026-08-13，Spike 6/6；桌面端发布含单机 Web 前端 28 文件）
> 目标：补齐需求 7.2 单机版内置 Web 的核心闭环——多部门共用同一采集站时的登录、权限隔离、文件查询与基础管理。

## 1. 交付内容

**后端**（[Station.Desktop.WebHost](../../src/Station.Desktop/Station.Desktop.WebHost)）

- Startup 增加 Cookie 认证（401/403 直接返回状态码），复用桌面端同一套账号/角色/权限体系；
- `AuthController`：登录（失败锁定 5 次/15 分钟复用服务层，登录审计带来源 IP）、退出、当前会话；
- `StandaloneAdminController`：部门 CRUD、用户 CRUD/重置密码/角色分配、角色 CRUD/权限勾选、记录仪列表与**写入绑定**（复用 RecorderService + 当前采集源根目录写 ini）、报警列表/处置——全部按权限码 + 部门树数据范围；
- `FilesController`：文件查询（关键字/类型/采集状态/上传状态/时间，元数据含 SM3/原始时间/记录仪/部门）+ CSV 导出；
- `AuditLogsController`：审计日志筛选查询 + CSV 导出（来源 IP 脱敏）；
- **修复**：WebHost 项目未包含 wwwroot，桌面端发布包此前不含单机 Web 前端 → csproj 增加 Content 拷贝（发布产物现含 28 个前端文件）。

**前端**（[station-web](../../station-web)，Vue3 + Element Plus，构建产物进 WebHost wwwroot）

- 登录页 + 布局（执法蓝白）；七个页面：文件查询（在线预览）、部门管理、用户管理、角色管理、记录仪管理（写入绑定确认）、报警中心、审计日志；菜单与按钮按会话权限显隐。

## 2. 验证结果（Spike 6/6）

| 场景 | 结果 |
|---|---|
| 未登录访问接口 → 401；首页返回 Vue 入口 | ✅ |
| admin 全量：文件 2、部门 3（ROOT+一队+一组）、用户 3、角色 4 | ✅ |
| 数据隔离：负责人见本部门及下级 2 文件，操作员仅本人 1 文件 | ✅ |
| 操作员访问部门管理 → 403 | ✅ |
| 审计查询 + 导出（含掩码 IP、不含原始 IP） | ✅ |
| 记录仪/报警接口 200 | ✅ |
| 桌面端发布包包含单机 Web 前端（wwwroot 28 文件） | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.StandaloneWeb -c Release
```

## 3. 说明与后续（M38 建议）

- 访问方式：默认监听本机（127.0.0.1:5000）；局域网访问开关 + 开启后 HTTPS/访问日志按需求 7.2 属剩余项；
- 单机 Web 导入（部门/用户 CSV 模板、错误报告）与更多导出可复用平台 M30/M32 模式；
- 记录仪"写入绑定"在模拟源/已插入 UMS 设备场景生效，真机接入后按 10.1 确认流程验收；
- 桌面端已运行的实例需重启后才加载新的内置 Web（含前端）。
