# M23：文件模块数据权限闭环 + 采集站部门归属管理

> 状态：✅ 通过（2026-08-13，Spike 12/12）
> 目标：把 M22 的部门树数据权限从站/报警完整铺到文件模块（列表/详情/预览），并补齐平台侧"采集站部门归属调整"管理能力（端点+界面+权限）。

## 1. 交付内容

- **权限目录扩展**：`PermissionCodes` 新增 `station:view`（查看采集站）、`station:manage`（管理采集站），部门负责人预置角色补 `station:view`；
- **AuthSeeder 幂等升级**：存量库（非空权限表）启动时只补新增权限码，系统角色按目录补齐缺失权限，无需重建数据库即可获得新权限；
- **文件模块数据权限**：`PlatformFilesController` 加 `[Authorize]`，列表按 `DeptId` + 部门树过滤，详情/预览对越权文件统一返回 404（不泄露存在性），未登录 401；
- **部门树端点**：`GET /api/v1/depts`（需 `dept:view`），供前端下拉与后续组织管理使用；
- **采集站归属调整**：`PUT /api/v1/stations/{stationId}/dept`（需 `station:manage`，并校验当前用户数据范围与目标部门有效性）；调整后**历史文件/报警保留上报时归属快照**，仅影响后续上报；
- **平台前端**：文件列表增加部门筛选下拉；采集站表格增加部门列+归属下拉+保存按钮（按会话权限显隐）；各接口未登录返回 401 提示；
- 共享数据权限解析收敛到 `DataScopeHelper`，站/报警/文件三个控制器同一套逻辑。

## 2. 验证结果（Spike 12/12）

| 场景 | 结果 |
|---|---|
| 未登录访问文件列表 → 401 | ✅ |
| admin 全量：站 2 / 报警 2 / 文件 2 / 部门树可用 | ✅ |
| 操作员@一组：仅见本组 1 站 1 报警 1 文件 | ✅ |
| 操作员访问部门树 → 403；越权文件详情 → 404；本组详情 → 200 | ✅ |
| 负责人@一队：本部门及下级（一组）2 站 2 报警 2 文件 | ✅ |
| 负责人改站归属 → 403（无 station:manage） | ✅ |
| admin 改 ST-B 归属 → 200，deptId 生效 | ✅ |
| 归属调整后历史文件按上报时快照保留（操作员仍见原文件） | ✅ |
| 文件列表按部门筛选参数生效 | ✅ |

验证代码：[spikes/Station.Spike.PlatformDataScopeFiles](../../spikes/Station.Spike.PlatformDataScopeFiles/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.PlatformDataScopeFiles -c Release
```

## 3. 说明与后续（M24 建议）

1. 跨站汇总统计（按部门树过滤的台账/采集量/报警趋势报表，数据权限与 M22/M23 同一套 `DataScopeHelper`）；
2. 真实 UMS/MTP 采集源、SFTP 真实服务器联调、平台前端 Vue 工程化仍待办；
3. 远程指令 SM2 验签升级为 PlatformCommand 读取时实时签名校验（当前为下发时生成签名入库存证）。
