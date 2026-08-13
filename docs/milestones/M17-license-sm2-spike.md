# M17：授权签名升级 SM2 非对称（内部工具私钥签名 / 采集站公钥验签）

> 状态：✅ 通过（2026-08-13，SQLite + Kingbase 8/8）
> 目标：把 M16 授权文件签名从 HMAC-SM3（对称）升级为 **SM2 非对称**——内部工具私钥签名、采集站仅内置公钥验签，符合国密合规要求。

## 1. 交付内容

### 基础设施（Station.Infrastructure.Security）

- `Sm2LicenseSigner`：SM2（sm2p256v1，国密默认 ID `1234567812345678`）：
  - `CreateKeyPair()`：生成 (私钥PEM, 公钥PEM)；
  - `Sign(privateKeyPem, text)`：私钥签名（DER → Base64）；
  - `Verify(publicKeyPem, text, signature)`：公钥验签；
  - 密钥格式：`SM2-PRIVATE:` / `SM2-PUBLIC:` 前缀 + DER(Base64)，规避 PemWriter 格式兼容问题。

### 授权模块（Station.Application.Licensing）

- `LicenseOptions`：`PublicKeyPem`（采集站内置公钥）/ `PrivateKeyPem`（仅内部生成工具），生产部署分别配置；
- `LicenseFileCodec`：签名/验签改走 SM2；
- `LicenseGenerator`：使用私钥签名（未配置私钥则拒绝生成）；
- `LicenseService.ActivateAsync`：使用公钥验签（未配置公钥提示"无法验签"）。

## 2. 验证结果（SQLite + Kingbase × 8 项）

| 验证项 | SQLite | Kingbase |
|---|---|---|
| SM2 签名/验签（含篡改拒绝） | ✅ | ✅ |
| 机器指纹采集 | ✅ | ✅ |
| 内部工具生成（SM2 签名）+ 激活 | ✅ | ✅ |
| 篡改签名拒绝 | ✅ | ✅ |
| 换硬件（指纹不匹配）拒绝 | ✅ | ✅ |
| 到期 → Locked + 采集被拒 | ✅ | ✅ |
| 采集进行中授权到期 → 任务 Interrupted | ✅ | ✅ |
| 清理 | ✅ | ✅ |

验证代码：[spikes/Station.Spike.License](../../spikes/Station.Spike.License/Program.cs)（运行时生成 SM2 密钥对注入配置）

```powershell
dotnet run --project spikes/Station.Spike.License -c Release -- --db sqlite
dotnet run --project spikes/Station.Spike.License -c Release -- --db kingbase
```

## 3. 生产密钥流程

1. 内部工具生成 SM2 密钥对（`Sm2LicenseSigner.CreateKeyPair()`）；
2. **私钥**保留在内部工具（生成授权文件）；
3. **公钥**写入采集站 `Station:License:PublicKeyPem`（部署配置）；
4. 内部工具用私钥签发 `station.lic` → 交付客户 → 采集站导入激活（公钥验签 + 硬件指纹校验）。

## 4. 后续（M18 建议）

- 真实 UMS/MTP 采集源 + SFTP 真实服务器联调收尾；
- 平台前端 Vue 工程化（station-platform-web）；
- 远程指令 SM2 签名/验签（契约 `RemoteCommand.Signature` 已预留）。
