# M20：平台报警/授权管理（查询 API + 最小前端，数据链路已就绪）

> 状态：✅ 通过（2026-08-13，Spike 4/4）
> 目标：把 M19 上报的数据在平台侧管理化：报警查询、采集站授权状态查询，前端四页签（文件/报警/采集站/指令）。

## 1. 交付内容

### 平台端（Station.Platform.Api）

- `PlatformAdminController`：
  - `GET /api/v1/alerts`：报警查询（站/级别/状态筛选 + 分页，时间倒序）；
  - `GET /api/v1/stations`：采集站列表（含 **授权状态/到期时间/剩余天数**/系统/架构/版本/注册时间）；
- `PlatformAlertReport` 增加 `Status`（平台侧报警处置状态，默认 Pending）。

### 前端（wwwroot/index.html 重构）

- 四页签：**文件列表 / 报警管理 / 采集站授权 / 远程指令**；
- 报警管理：站/级别筛选 + 表格（级别/类型/来源/内容/时间）；
- 采集站授权：表格（授权状态/到期/剩余天数），平台一目了然各站授权情况。

## 2. 验证结果（Spike 4/4）

| 步骤 | 结果 |
|---|---|
| 激活授权 → 心跳上报（平台 Activated，剩余 29 天） | ✅ |
| 报警上报 ×2 → 平台落库 | ✅ |
| 平台查询 API：报警总数 2、站授权状态 Activated/29 天 | ✅ |
| 清理 | ✅ |

验证代码：[spikes/Station.Spike.PlatformAdmin](../../spikes/Station.Spike.PlatformAdmin/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.PlatformAdmin -c Release
```

## 3. 联调入口

```powershell
dotnet run --project src/Station.Platform/Station.Platform.Api -c Release
# 浏览器 http://127.0.0.1:5100/ → 四页签切换
```

## 4. 说明与后续（M21 建议）

1. 平台报警处置（确认/处理/关闭）与 RBAC 数据权限为下一步；
2. 真实 UMS/MTP 采集源、SFTP 真实服务器联调、平台前端 Vue 工程化仍待办；
3. 授权到期前平台预警（剩余天数 < N 报警）可复用报警链路。
