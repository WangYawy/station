# M38：单机 Web 补齐（局域网/HTTPS/访问日志 + CSV 导入）

> 状态：✅ 通过（2026-08-13，Spike 5/5 + M37 回归 6/6）
> 目标：补齐需求 7.2 单机版内置 Web 剩余 P0——访问方式（默认本机、可配局域网 + HTTPS/访问日志）与导入导出中的 CSV 导入（模板、校验、错误报告）。

## 1. 交付内容

**访问方式与安全**

- 新增 [WebOptions](../../src/Station.Desktop/Station.Desktop.WebHost/WebOptions.cs)（`Station:Web`）：监听地址（默认 `127.0.0.1`）、端口（5000）、`EnableLan`、`EnableHttps`、HTTPS 端口与证书路径/密码；
- 宿主按配置 `UseUrls`：默认仅本机；`EnableLan=true` 监听 `0.0.0.0` 且强制 HTTPS（Kestrel 证书）+ **访问日志**——局域网模式下所有 `/api` 请求写入审计（`web.access`：路径、方法、状态、来源 IP）；
- 修复 `HostBuilderFactory` 配置传递（预构建配置 + 环境变量 → Web 选项生效）。

**CSV 导入（模板、校验、错误报告、审计）**

- 新增 [ImportsController](../../src/Station.Desktop/Station.Desktop.WebHost/Controllers/ImportsController.cs)：
  - `POST /imports/depts`（`dept:manage`）：编码/名称/上级编码/排序，上级校验、重名校验；
  - `POST /imports/users`（`user:manage`）：工号/姓名/部门编码/角色编码(分号)/初始密码，创建用户+账号（账号名=工号，默认密码 `Station@123`）；
  - `POST /imports/recorder-bindings`（`recorder:manage`）：序列号/用户工号/部门编码，更新台账绑定；
  - 返回 `{total, success, failed, errors:[{line,message}]}`，部分成功不阻塞，导入操作落审计；
- WebHost 增加纯文本输入格式器（text/plain、text/csv）；
- **修复**：Web 创建用户未建登录账号（共享 UserService 要求账号名+密码同时非空）→ 创建时账号名=工号、默认密码 `Station@123`。

**前端**

- 部门/用户/记录仪页新增"导入"按钮（选择 CSV → 结果弹窗：成功/失败 + 行号错误表）与"下载模板"（UTF-8 BOM）。

## 2. 验证结果（Spike 5/5 + M37 回归 6/6）

| 场景 | 结果 |
|---|---|
| 默认配置仅监听 127.0.0.1（非 0.0.0.0） | ✅ |
| 部门导入 2 成功 1 失败（上级不存在，行号准确） | ✅ |
| 用户导入 1 成功 1 失败（坏部门）；导入用户默认密码可登录 | ✅ |
| 记录仪绑定导入成功，台账绑定更新 | ✅ |
| 默认模式不写访问日志 | ✅ |
| `EnableLan=true` 监听 0.0.0.0 | ✅ |
| 局域网模式访问日志：`web.access` 含 `/api/v1/health`、来源 IP 127.0.0.1 | ✅ |
| M37 回归：登录/RBAC/数据隔离/审计导出 6/6 | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.StandaloneWebImport -c Release
dotnet run --project spikes/Station.Spike.StandaloneWeb -c Release
```

## 3. 说明与后续

- 生产局域网部署：`appsettings.json` 配置 `Station:Web`（EnableLan=true、EnableHttps=true、证书路径）；未配置证书时不会启用 HTTPS，属安全校验项；
- 单机 Web 导入/导出（文件、审计 CSV）与平台 M30/M32 同一套模式，后续如需 Excel 可复用；
- 桌面端重启后生效；单机 Web 其余 P0 缺口（SM4 缓存加密、本地备份、定时采集等）见基线差异清单，按优先级继续。
