# M22：平台组织数据权限（部门树过滤：上级看下级、同级隔离）

> 状态：✅ 通过（2026-08-13，Spike 3/3）
> 目标：按需求"上级可见全部下级组织数据，下级不可见上级/平级，同级隔离"，在平台查询（采集站/报警）落地部门树数据过滤。

## 1. 交付内容

- 契约：`StationRegistrationRequest` 增加 `DeptId`（采集站归属部门，平台可后续调整）；
- `PlatformStation` / `PlatformAlertReport` / `PlatformFileMetadata` 增加 `DeptId`（上报时从站归属继承）；
- 平台查询端点（`GET /api/v1/stations`、`GET /api/v1/alerts`）按当前用户角色数据范围过滤：
  - 复用共享 `DataScopeProvider`（组织树：本部门及下级/仅本部门/仅本人/All）；
  - 操作员=仅本部门数据；部门负责人=本部门及下级；管理员/审计员=全量。

## 2. 验证结果（Spike 3/3）

| 场景 | 采集站可见 | 报警可见 | 结果 |
|---|---|---|---|
| admin（全量） | 2 | 2 | ✅ |
| 操作员@一组（仅本组） | 1（本组） | 1 | ✅ |
| 部门负责人@一队（本部门及下级一组） | 2 | 2 | ✅ |

验证代码：[spikes/Station.Spike.PlatformDataScope](../../spikes/Station.Spike.PlatformDataScope/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.PlatformDataScope -c Release
```

## 3. 说明与后续（M23 建议）

1. 文件列表 `GET /api/v1/files` 同模式接入部门过滤（与站/报警一致）；
2. 采集站部门归属管理（平台设置端点/界面）与上级跨站汇总统计；
3. 真实 UMS/MTP 采集源、SFTP 真实服务器联调、平台前端 Vue 工程化仍待办。
