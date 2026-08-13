# M30：台账/报表 CSV 导出（权限 + 数据范围 + 脱敏）

> 状态：✅ 通过（2026-08-13，Spike 7/7 + M27 前端回归 3/3）
> 目标：按需求"列表/报表 Excel 或 CSV 导出，导出按权限过滤并脱敏"落地统一 CSV 导出能力。

## 1. 交付内容

**后端**（新增 [PlatformExportController](../../src/Station.Platform/Station.Platform.Api/Controllers/PlatformExportController.cs)，`/api/v1/exports/*`）

- 七个导出端点：`files` / `alerts` / `stations` / `recorders` / `audit-logs` / `stats-trend` / `stats-stations`；
- 与对应列表同一套权限码（`file:view` / `alert:view` / `station:view` / `recorder:view` / `audit:export`）+ 部门树数据范围过滤，导出上限 1 万行；
- CSV 采用 **UTF-8 BOM**（Excel 直接打开中文不乱码）+ RFC4180 转义（逗号/引号/换行字段加引号）；
- **审计导出脱敏**：来源 IP 末段掩码（`127.0.0.1` → `127.0.0.*`）；
- 文件名带时间戳（如 `文件台账_20260813_183000.csv`，RFC5987 编码）；
- 修复：`ControllerBase.Forbid()` 在 Cookie 认证下会 302 到默认拒绝页（表现为 404），统一改为显式 `403`。

**前端**

- 新增 `downloadCsv` 辅助（解析 `filename*` 中文文件名、401 跳登录）；
- 六个页面接入导出按钮：文件检索、报警中心、采集站管理、记录仪管理、审计日志（系统管理）、统计报表（导出趋势/导出排行），导出按当前筛选条件生效。

## 2. 验证结果（Spike 7/7）

| 场景 | 结果 |
|---|---|
| 未登录导出 → 401 | ✅ |
| 文件导出：UTF-8 BOM、中文表头、逗号文件名 `"a,b.mp4"` 转义、Content-Disposition 带 .csv | ✅ |
| 报警导出：级别/严重/内容字段 | ✅ |
| 记录仪导出：R-001、白名单"是" | ✅ |
| 统计导出：趋势（日期/文件数/容量）+ 排行（ST-A/在线） | ✅ |
| 审计导出脱敏：含 `127.0.0.*`，不含原始 `127.0.0.1` | ✅ |
| 权限与数据范围：操作员文件导出仅表头（本组无数据）、审计导出 403 | ✅ |
| M27 前端资源托管回归 3/3 | ✅ |

验证代码：[spikes/Station.Spike.PlatformExport](../../spikes/Station.Spike.PlatformExport/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.PlatformExport -c Release
```

## 3. 说明与后续（M31 建议）

1. 真实 UMS/MTP 采集源 + SFTP 真实服务器联调（需真实设备）；
2. 记录仪使用轨迹 / 生命周期预警列 P1；
3. 组织/用户 Excel 导入（模板、校验、错误报告）列 P1；
4. Excel（xlsx）导出与统计报表 PDF 列 P1。
