# M31：记录仪生命周期预警 + 使用轨迹

> 状态：✅ 通过（2026-08-13，Spike 5/5 + M27 前端回归 3/3）
> 目标：按原型补齐设备中心记录仪管理——生命周期预警（未绑定/非白名单/长期未使用）与使用轨迹（按日趋势 + 按采集站聚合）。

## 1. 交付内容

**后端**（[PlatformRecordersController](../../src/Station.Platform/Station.Platform.Api/Controllers/PlatformRecordersController.cs)）

- 台账列表新增**生命周期预警**计算与筛选：
  - `no_binding`（未绑定用户）、`not_whitelisted`（非白名单）、`idle`（最后上报超过 N 天，`Platform:RecorderIdleDays` 默认 30）；
  - 列表返回 `lifecycleWarnings`，支持 `warning=true` 筛选出有预警记录仪；
- 新增 `GET /api/v1/recorders/{id}/trail?days=30` **使用轨迹**：
  - 近 N 天按日使用量（文件数/容量，缺失日补 0）；
  - 按采集站聚合（站编码、文件数、容量、首次/最近使用，按文件数倒序）；
  - 与列表同一套 `recorder:view` 权限 + 部门树数据范围（轨迹只统计用户可见部门的文件）。

**前端**（记录仪管理页）

- 台账新增"生命周期预警"列（未绑定/非白名单/长期未使用标签）与"有预警"筛选；
- 每行新增"轨迹"按钮：弹窗展示近 30 天按日使用柱状图（ECharts 按需）+ 按采集站聚合表；轨迹按钮对只读角色同样可见。

## 2. 验证结果（Spike 5/5）

| 场景 | 结果 |
|---|---|
| 未登录访问轨迹 → 401 | ✅ |
| 预警计算：R-001（绑定+白名单+近期）= 无；R-IDLE（未绑定/非白名单/40天未上报）= 三项 | ✅ |
| 预警筛选 `warning=true` 仅返回 R-IDLE | ✅ |
| 使用轨迹：按日=今日2/昨日1/前2天0；按站=ST-A:2、ST-B:1 | ✅ |
| 轨迹数据范围：仅本部门角色只见 ST-B（1 文件），台账只见范围内记录仪 | ✅ |
| M27 前端资源托管回归 3/3 | ✅ |

验证代码：[spikes/Station.Spike.PlatformRecorderLifecycle](../../spikes/Station.Spike.PlatformRecorderLifecycle/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.PlatformRecorderLifecycle -c Release
```

## 3. 说明与后续（M32）

- 生命周期预警当前为规则型（绑定/白名单/闲置阈值），后续可扩展累计使用量、维修周期等；
- M32：组织/用户 CSV 导入（模板、校验、错误报告）。
