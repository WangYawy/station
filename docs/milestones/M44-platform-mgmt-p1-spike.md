# M44：平台管理增强 P1（采集站维修/报废状态 + 记录仪/采集站 CSV 导入）

> 状态：✅ 通过（2026-08-13，Spike 4/4）
> 目标：P1 增强第一批——采集站运行状态（正常/维修/报废）与记录仪、采集站台账 CSV 导入（模板/校验/错误报告/审计）。

## 1. 交付内容

- **采集站运行状态**：`StationOperationalStatus`（正常/维修/报废），`PlatformStation.OperationalStatus`；`PUT /api/v1/stations/{id}/status`（`station:manage` + 数据范围 + 审计）；`EnsureColumn` 支持指定列类型（int），存量库启动自动补列；
- **记录仪 CSV 导入**：`POST /imports/recorders`（`recorder:manage`）模板 `序列号,型号,协议,白名单`，协议支持 UMS/MTP/私有SDK（或 0/1/2），白名单是/否；
- **采集站 CSV 导入**：`POST /imports/stations`（`station:manage`）模板 `站编号,系统,架构,版本,部门编码`，部门编码校验；
- 均返回 `{total,success,failed,errors:[{line,message}]}`，部分成功不阻塞，导入落审计；
- **前端**：采集站表格新增"运行状态"列（有权限下拉即时修改，否则标签展示）；采集站/记录仪页新增导入按钮 + 模板下载 + 结果弹窗。

## 2. 验证结果（Spike 4/4）

| 场景 | 结果 |
|---|---|
| 采集站状态修改（admin 200，状态生效；操作员 403） | ✅ |
| 记录仪导入 2 成功 1 失败（重复序列号），白名单生效 | ✅ |
| 采集站导入 1 成功 1 失败（坏部门），部门挂接正确 | ✅ |
| 审计留痕：station.status / import.stations / import.recorders | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.PlatformMgmtP1 -c Release
```

## 3. 说明与后续（M45）

- M45：文件归属修正（含 SM2 签名留存）；
- 继续 P1：白名单等全量配置下发、xlsx/PDF 导出、SignalR 实时推送。
