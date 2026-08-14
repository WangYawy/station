# M50：平台采集站详情页

> 状态：✅ 通过（2026-08-14，Station.Spike.StationDetail 9/9）
> 目标：按原型"监控管理平台-采集站详情"落地平台采集站详情页，数据全部来自平台真实上报（文件/报警/记录仪/指令/配置/趋势），结合 RBAC 数据范围与 P0 能力裁剪。

## 1. 页面结构（对齐原型，按实际数据裁剪）

### 顶部

- 返回采集站列表、站编号 + 在线/离线 + 运行状态（正常/维修/报废）标签；
- 元信息：归属部门、软件版本、最后心跳、授权状态/剩余天数；
- 操作：刷新、重启服务（RestartService 指令，station:manage）。

### 统计概览（4 卡）

- 文件总量（个数/容量）、今日采集（个数/容量）、待处理报警（严重/警告分布）、关联记录仪（总数/白名单数）——全部来自平台聚合，非原型模拟数。

### Tabs

1. **设备信息**：近 14 天采集趋势（ECharts，站过滤）；设备信息卡（编号/部门/系统/架构/版本/USB 口/授权/注册/心跳/站内 Web/机器指纹四项）；远程指令下发（P0 六类：重启服务/清理缓存/同步配置/一键自检/停止采集/开始采集，SM2 签名说明 + 最近 5 条回执）；关联记录仪 chips；最近报警 5 条；最近文件 5 条。
2. **策略配置**：采集策略（自动采集/采集后擦除/跳过已采集）表单 + 下发；设备自检入口（下发 RunSelfCheck 指令，回执见设备信息）；配置下发版本记录（含最新载荷预填表单）。
3. **操作授权**：归属部门调整（station:manage）+ RBAC/部门树数据权限说明；用户域/白名单全量下发（M46 sync-domain，user:manage）。
4. **记录仪白名单**：按站过滤的记录仪列表（序列号/协议/绑定用户部门/文件数/最后上报/白名单状态），加入/移出白名单（recorder:manage）；同步白名单 = 用户域下发。
5. **存储管理**：已上报文件按存储位置分布（位置/文件数/容量/占比进度条）；存储配置（熔断阈值/重试次数）下发。

## 2. 后端增强（Station.Platform.Api）

- `GET /api/v1/stations/{id}`（PlatformAdminController）：站基础信息 + 部门名 + 文件/今日采集/待处理报警（按级别）/记录仪/白名单/存储位置分布聚合 + 在线判定（`Platform:OnlineTimeoutSeconds`），数据范围过滤；
- `GET /api/v1/recorders` 新增 `stationId` 过滤（`LastStationId`）；
- **安全补齐**：
  - `PlatformConfigsController` 补 `[Authorize]`：配置查询需 station:view、下发需 station:manage（此前完全未鉴权）；
  - `PlatformCommandsController.Dispatch` 补站存在校验 + 数据范围检查（此前任意登录用户可向任意站下发指令）。

## 3. 前端（station-platform-web）

- 新增 `StationDetailView.vue`（路由 `/stations/:id`），实时事件（station/file/alert/command）自动刷新；
- 采集站列表站编号可点击进入详情；
- 指令/配置/白名单/归属等操作按权限显隐（station:manage / recorder:manage / user:manage / dept:view）。

## 4. 验证（Station.Spike.StationDetail 9/9，MySQL 平台库）

| 场景 | 结果 |
|---|---|
| 详情聚合：文件 3/今日 2/待处理报警 2/记录仪 3（白名单 2）/存储位置 2/部门名/CPU 序列号 | ✅ |
| 待处理报警级别分布（警告 1、严重 1，已关闭不计） | ✅ |
| 记录仪按站过滤（3 条且 LastStationId 一致） | ✅ |
| 配置查询未登录 401；管理员 200 / 操作员 403；下发管理员 200 / 操作员 403 | ✅ |
| 指令下发 200；数据范围通过 | ✅ |
| 采集趋势站过滤（14 点，今日 2） | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.StationDetail -c Release
```

## 5. 后续

- 采集站实时资源（CPU/内存/磁盘）与 USB 端口状态需采集站侧新增遥测上报后接入（当前以采集趋势/文件/报警替代原型模拟卡）；
- 采集任务队列（进行中/暂停/取消）需采集站任务状态上报后接入；
- 存储目标在线/熔断状态需采集站存储探针上报后接入（当前展示已上报文件的位置分布）。
