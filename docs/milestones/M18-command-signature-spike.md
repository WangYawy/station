# M18：远程指令 SM2 签名/验签（平台私钥签名 → 采集站公钥验签后执行）

> 状态：✅ 通过（2026-08-13，Spike 3/3）
> 目标：按需求"远程指令带 SM2 签名，采集站验签执行"，把 M14 的指令链路升级为**防伪造**：平台下发签名指令，采集站验签不通过则拒绝执行并回执 Failed。

## 1. 交付内容

### 契约

- `RemoteCommandSignature.Canonical(command)`：指令规范化字符串（CommandId|StationId|Type|PayloadJson|IssuedAt(UTC秒)|TimeoutSeconds）。

### 平台端

- `PlatformCommandsController.Dispatch`：用 SM2 私钥（`Platform:Command:PrivateKeyPem` 或 `PrivateKeyPemFile`）对 Canonical 签名写入 `Signature`；未配置私钥时置 "unsigned"（联调占位）。

### 采集站

- `CommandVerifierOptions`（`Station:Command`：`PublicKeyPem` + `Required`）；
- `CommandSignatureVerifier`：验签（未配置公钥/Required=false 时跳过，兼容旧部署；生产 Required=true）；
- `CommandService`：**验签失败 → 拒绝执行 + 回执 Failed（"指令签名无效，拒绝执行"）**。

## 2. 验证结果（Spike 3/3）

| 步骤 | 结果 |
|---|---|
| 有效签名指令：验签通过 → 执行成功（自检 Succeeded） | ✅ |
| 篡改平台库签名：验签失败 → 拒绝执行 + 回执 Failed（"签名无效"） | ✅ |
| 清理 | ✅ |

验证代码：[spikes/Station.Spike.CommandSignature](../../spikes/Station.Spike.CommandSignature/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.CommandSignature -c Release
```

## 3. 关键排障沉淀

1. **Spike 子进程环境变量会被父进程继承**：Spike 为采集站设置的 `STATION__DB__*` 会让平台子进程也用错数据库，必须显式覆盖（或注入平台专属配置）；
2. **平台 exe 为硬编码路径**：修改平台代码后必须重建 `Station.Platform.sln`，否则 Spike 跑旧二进制（多轮"签名无效"假象的根因之一）；
3. **Canonical 时区**：签名 canonical 必须用 UTC 秒规范化（带时区序列化再反序列化会转 UTC，导致两端字符串不一致）。

## 4. 配置

```json
// 平台端
{ "Platform": { "Command": { "PrivateKeyPem": "", "PrivateKeyPemFile": "" } } }
// 采集站
{ "Station": { "Command": { "PublicKeyPem": "", "Required": true } } }
```

## 5. 后续（M19 建议）

- 真实 UMS/MTP 采集源 + SFTP 真实服务器联调收尾；
- 平台前端 Vue 工程化（station-platform-web）；
- 报警上报 SM2 签名（与指令同模式）与授权状态随心跳上报平台。
