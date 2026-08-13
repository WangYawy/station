# M32：组织/用户 CSV 导入（模板、校验、错误报告）

> 状态：✅ 通过（2026-08-13，Spike 4/4 + M27 前端回归 3/3）
> 目标：按需求"组织、用户导入：有模板、校验和错误报告"落地 CSV 导入，权限与数据范围与系统管理一致。

## 1. 交付内容

**后端**（新增 [PlatformImportController](../../src/Station.Platform/Station.Platform.Api/Controllers/PlatformImportController.cs)）

- `POST /api/v1/imports/depts`（`dept:manage`）：列 = 编码,名称,上级编码,排序；校验编码唯一（库内+文件内）、名称必填、上级部门存在/启用/在数据范围内、非全量角色不可建顶级部门；
- `POST /api/v1/imports/users`（`user:manage`）：列 = 工号,姓名,部门编码,角色编码(分号分隔),初始密码；校验工号唯一（用户+账号）、部门存在/在范围、角色存在/启用；每行独立事务创建用户+账号+角色（默认密码 `Station@123`）；
- **部分成功 + 错误报告**：返回 `{total, success, failed, errors:[{line, message}]}`，失败行不影响其他行导入；导入操作落审计（`import.depts` / `import.users`）；
- 新增纯文本输入格式器（text/plain、text/csv），支持 CSV 文本绑定。

**前端**（系统管理）

- 组织架构 / 用户管理标签页新增"导入"（选择 .csv 文件后展示结果弹窗：成功/失败数 + 行号错误表）与"下载模板"（UTF-8 BOM CSV，含示例行）。

## 2. 验证结果（Spike 4/4）

| 场景 | 结果 |
|---|---|
| 未登录导入 → 401 | ✅ |
| 部门导入：2 成功 + 2 失败（第 4 行上级不存在、第 5 行缺编码名称），错误行号准确 | ✅ |
| 用户导入：2 成功（含角色/无角色）+ 2 失败（第 4 行坏部门、第 5 行重复工号）；导入用户可用默认密码登录 | ✅ |
| 权限：负责人/操作员导入 → 403 | ✅ |
| M27 前端资源托管回归 3/3 | ✅ |

验证代码：[spikes/Station.Spike.PlatformImport](../../spikes/Station.Spike.PlatformImport/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.PlatformImport -c Release
```

## 3. 说明与后续（M33 建议）

1. 真实 UMS/MTP 采集源 + SFTP 真实服务器联调（需真实设备）；
2. 采集站 Excel 导入（与部门/用户同模式）列 P1；
3. xlsx 导入/导出与统计报表 PDF 列 P1；
4. 若进入交付准备：桌面端单文件发布、平台 Docker Compose、信创 x86_64/ARM64 构建验证。
