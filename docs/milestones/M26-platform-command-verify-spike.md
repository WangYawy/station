# M26：远程指令 SM2 读取时实时验签

> 状态：✅ 通过（2026-08-13，Spike 5/5）
> 目标：把指令签名从"下发时生成、入库存证、轮询原样返回"升级为"平台轮询读取时实时验签"，库内指令被篡改或未签名时拒绝下发并标记异常；同时补上指令下发接口的登录鉴权。

## 1. 交付内容

- **公钥推导**：`Sm2LicenseSigner.DerivePublicKey` 从私钥推导对应公钥，平台侧只需配置私钥即可对自身指令做读取时验签；
- **密钥解析收敛**：新增 `PlatformCommandKeys`，统一解析 `Platform:Command:PrivateKeyPem(PemFile)` 与 `PublicKeyPem(PemFile)`，公钥未显式配置时自动从私钥推导（同一密钥对）；
- **读取时实时验签**：`PollCommands` 读取待下发指令时逐条验签——
  - 验签通过 → 状态置 `Pulled` 并下发；
  - 验签失败（库内载荷/字段被篡改）→ 不下发，状态置 `Failed`，结果记录"指令签名校验失败（疑似被篡改），拒绝下发"；
  - 已配置公钥但指令未签名（`Signature='unsigned'`）→ 不下发，状态置 `Failed`，记录"未签名指令"；
  - 未配置任何密钥（开发模式）→ 跳过验签，保持原兼容行为；
- **指令下发接口补鉴权**：`PlatformCommandsController` 增加 `[Authorize]`（下发/列表需登录，未登录 401），与平台其他管理接口一致；
- 配置：`appsettings.json` 增加 `Platform:Command:PublicKeyPem`。

## 2. 验证结果（Spike 5/5）

| 场景 | 结果 |
|---|---|
| 未登录下发指令 → 401 | ✅ |
| 有效签名指令：读取时验签通过并下发，返回签名可用公钥验签 | ✅ |
| 篡改载荷（Type 被改）：读取时验签失败，不下发，状态 Failed + "签名校验失败" | ✅ |
| 未签名指令（Signature='unsigned' 且已配公钥）：不下发，状态 Failed + "未签名指令" | ✅ |
| 开发模式（无密钥）：兼容下发（Signature='unsigned'，跳过验签） | ✅ |

验证代码：[spikes/Station.Spike.PlatformCommandVerify](../../spikes/Station.Spike.PlatformCommandVerify/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.PlatformCommandVerify -c Release
```

## 3. 说明与后续（M27 建议）

1. 真实 UMS/MTP 采集源 + SFTP 真实服务器联调（采集侧闭环收尾，需真实设备）；
2. 前端按路由代码分割 + ECharts 按需引入（当前单包约 2.2MB）；
3. 系统管理模块（组织架构/用户权限/日志管理）与记录仪管理页列 P1；
4. 统计/台账导出（CSV/Excel，按权限过滤并脱敏）列 P1。
