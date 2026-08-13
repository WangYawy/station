# M19：报警/授权状态上报 SM2 签名验签（站私钥签名 → 平台公钥验签落库）

> 状态：✅ 通过（2026-08-13，Spike 4/4）
> 目标：上行上报（站→平台）与下行指令同等级安全：报警、授权状态经 SM2 签名，平台验签后落库；篡改签名拒绝并重试。

## 1. 交付内容

### 契约

- `AlertReport` 增加 `Signature`；`AlertReportSignature.Canonical`（UTC 秒规范化）；
- `LicenseStatusReport` + `LicenseStatusReportSignature.Canonical`。

### 采集站

- `AlertService.WriteAsync`：报警落库后**自动入 Outbox 上报平台**（SM2 签名，`Station:Reporting:PrivateKeyPem`），断网补报；
- `PlatformSyncWorker`：每轮**授权状态心跳上报**（Trial/Activated/到期 + 剩余天数，SM2 签名）；
- `SyncOutboxService` 新增 `license-status` 主题。

### 平台端

- `POST /api/v1/stations/{id}/alerts`：**验签**（`Platform:Reporting:PublicKeyPem` / `PublicKeyPemFile`），无效返回 400；
- `POST /api/v1/stations/{id}/license`：验签后更新 `platform_station` 的授权状态/到期时间/剩余天数；
- 平台可实时看到各采集站授权状态。

## 2. 验证结果（Spike 4/4）

| 步骤 | 结果 |
|---|---|
| 授权状态上报：激活 → 上报 → 平台 `LicenseStatus=Activated`（剩余 29 天） | ✅ |
| 报警自动上报：WriteAsync → Outbox → 平台验签落库（1 条） | ✅ |
| 篡改签名报警：平台 400 拒绝、不落库、Outbox 重试 | ✅ |
| 清理 | ✅ |

验证代码：[spikes/Station.Spike.ReportingSecurity](../../spikes/Station.Spike.ReportingSecurity/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.ReportingSecurity -c Release
```

## 3. 配置

```json
// 采集站（签名）
{ "Station": { "Reporting": { "PrivateKeyPem": "" } } }
// 平台（验签）
{ "Platform": { "Reporting": { "PublicKeyPem": "", "PublicKeyPemFile": "" } } }
```

## 4. 说明与后续（M20 建议）

1. 上行/下行均已 SM2 签名：指令（平台→站）+ 报警/授权状态（站→平台）；
2. Spike 排障再次印证：**修改平台代码后必须重建 Platform.sln**（硬编码 exe 路径 + 构建跳过曾导致多轮 400 假象）；
3. 后续：真实 UMS/MTP 采集源、SFTP 真实服务器联调、平台前端 Vue 工程化。
