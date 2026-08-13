# M24：跨站汇总统计（概览/采集趋势/采集排行/报警统计）

> 状态：✅ 通过（2026-08-13，Spike 11/11）
> 目标：落地需求"基础统计：采集量、设备在线率、报警趋势、采集排行"，全部按部门树数据权限过滤（与 M22/M23 同一套 `DataScopeHelper`）。

## 1. 交付内容

- **在线率数据源**：`PlatformStation` 新增 `LastHeartbeatAt`，站→平台任意入站请求打点（注册/配置同步/元数据上报/报警上报/授权上报/指令轮询/指令回执）；在线判定 = 心跳在超时窗口内（默认 5 分钟，`Platform:OnlineTimeoutSeconds` 可配）；
- **跨库补列机制**：`IDbDialect` 增加时间列类型映射，`DatabaseInitializer.EnsureColumn` 幂等补列，平台启动自动给存量 `platform_station` 补 `LastHeartbeatAt`（MySQL 实测，PG/Kingbase 通道验证），后续新增列沿用同一机制；
- **统计端点**（均需 `file:view` + 部门树过滤）：
  - `GET /api/v1/stats/overview`：站数/在线/离线、文件总数/容量、今日采集、视频数、待处理报警/报警总数；
  - `GET /api/v1/stats/collection-trend?days=&stationId=&deptId=`：近 N 天（默认 14，上限 365）逐日文件数与容量，缺失日补 0；
  - `GET /api/v1/stats/stations`：采集排行，按文件数倒序（文件数/容量/报警数/最后采集/在线状态/部门）；
  - `GET /api/v1/stats/alerts`：报警按级别/处置状态/类型汇总；
- **平台前端**：新增"统计报表"页（概览卡片、采集趋势条形图、采集排行表、报警统计表）；
- **修复 SqlSugar 查询串扰陷阱**：同一 `ISugarQueryable` 上先派生 `Where` 再复用原对象计数，过滤条件会原地串扰（表现为"报警总数=待处理数"）；统计端点改为每个口径从 `AsQueryable()` 独立重建，并在文档沉淀该规范。

## 2. 验证结果（Spike 11/11）

| 场景 | 结果 |
|---|---|
| 未登录访问概览 → 401 | ✅ |
| admin 概览：2 站（1 在线）、5 文件、15MB、今日 3 文件、视频 3、报警 4（待处理 2） | ✅ |
| 操作员@一组：1 站、2 文件、9MB、报警 2（列表同步） | ✅ |
| 负责人@一队：本部门及下级 2 站、5 文件、报警 4 | ✅ |
| 采集趋势近 4 天逐日：今日 3 / 昨日 1 / 前2天 0 / 前3天 1（容量 7MB/3MB/0/5MB） | ✅ |
| 趋势按部门筛选（deptId=grp1） | ✅ |
| 采集排行 ST-A(3 文件) 在 ST-B(2 文件) 前，在线/离线正确 | ✅ |
| 报警统计：级别 0x1、1x2、2x1；状态/类型分组正确 | ✅ |
| 心跳打点：指令轮询后 ST-B 由离线变在线（在线数 1→2） | ✅ |
| PostgreSQL / Kingbase `ALTER TABLE ADD COLUMN timestamp` 补列机制 | ✅ |

验证代码：[spikes/Station.Spike.PlatformStats](../../spikes/Station.Spike.PlatformStats/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.PlatformStats -c Release
```

## 3. 说明与后续（M25 建议）

1. 平台前端 Vue 工程化（替换当前单文件 HTML，`station-platform-web`）；
2. 真实 UMS/MTP 采集源、SFTP 真实服务器联调；
3. 远程指令 SM2 验签升级为 PlatformCommand 读取时实时签名校验（当前为下发时生成签名入库存证）；
4. 统计/台账导出（CSV/Excel，按权限过滤并脱敏）列 P1；
5. 组织、用户、记录仪、采集站台账管理界面（当前为种子数据）列 P1。
