# M29：平台记录仪台账（归集 / 白名单 / 绑定 + WriteBinding 指令闭环）

> 状态：✅ 通过（2026-08-13，Spike 7/7 + M27 前端回归 3/3）
> 目标：补齐平台设备中心的记录仪管理——台账自动归集、白名单、重新绑定，并通过远程指令让采集站更新本地绑定（接入时自动写 ini）。

## 1. 交付内容

**后端（平台）**

- `PlatformRecorder`（platform_recorder）：全站记录仪台账，随文件元数据上报自动归集——序列号唯一、首次/末次上报、最近采集站、归属部门（数据权限）、文件数/容量/最近采集时间；
- `ReportMetadata` 增加台账 upsert：**重复上报不重复累计**；
- `PlatformRecordersController`：
  - `GET /api/v1/recorders`（`recorder:view` + 部门树数据范围 + 关键字/白名单/绑定状态筛选）；
  - `PUT /api/v1/recorders/{id}/whitelist`（`recorder:manage`）；
  - `PUT /api/v1/recorders/{id}/bind`（`recorder:manage`）：更新绑定用户/部门，并向最近采集站下发 `WriteBinding` 指令（payload camelCase，M26 读取时实时验签覆盖）；写操作落审计；
- 指令扩展：`CommandType.WriteBinding = 6`；`PlatformCommandKeys.Sign` 统一签名（未配私钥返回 `unsigned` 开发模式）；
- 权限：新增 `recorder:view` / `recorder:manage`（管理员全量、部门负责人查看、审计员全局只读），AuthSeeder 幂等升级自动补齐；
- **修复 SqlSugar 可空布尔筛选陷阱**：`bound != true` 这类表达式会被参数化为 `NULL <> true`，在 MySQL 中结果为 NULL 导致全表被过滤；改为参数非空时才追加 `Where`。

**桌面端（采集站）**

- `CommandExecutor` 增加 `WriteBinding` 处理：按 `userNo` 解析本机同步用户，更新/新建 `station_recorder` 台账（绑定用户、部门、白名单）；记录仪下次接入时由 `RecorderIdentificationService` 自动重写 `station_bind.ini`（复用既有识别闭环）；用户未同步时返回失败原因。

**前端**

- 新增"记录仪管理"菜单（按 `recorder:view` 显隐，懒加载 6.1KB）：台账列表（文件数/容量/最近采集/绑定用户部门）、白名单开关、重新绑定对话框（用户工号 + 可选部门，成功后显示下发的指令 ID）。

## 2. 验证结果（Spike 7/7）

| 场景 | 结果 |
|---|---|
| 未登录访问台账 → 401 | ✅ |
| 上报自动归集：R-001=2文件/5MB、R-002=1文件/4MB，重复上报不累计 | ✅ |
| 白名单开关生效 | ✅ |
| 绑定：台账更新 + WriteBinding 指令下发（payload 含序列号/用户/部门） | ✅ |
| 台账绑定字段（zhangsan / GRP1） | ✅ |
| 数据范围：负责人见 2 台、操作员 403 | ✅ |
| 桌面端执行 WriteBinding：指令 Succeeded、本地台账 BoundUserId=1 且入白名单 | ✅ |
| M27 前端资源托管回归 3/3 | ✅ |

验证代码：[spikes/Station.Spike.PlatformRecorders](../../spikes/Station.Spike.PlatformRecorders/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.PlatformRecorders -c Release
```

## 3. 说明与后续（M30 建议）

1. 真实 UMS/MTP 采集源 + SFTP 真实服务器联调（需真实设备；届时验证真机接入时 ini 自动重写）；
2. 记录仪使用轨迹 / 生命周期预警（原型：使用轨迹热度图、生命周期预警）列 P1；
3. 统计/台账导出（CSV/Excel，按权限过滤并脱敏）列 P1；
4. 组织/用户 Excel 导入列 P1。
