# M39：国密 SM4 加密基础设施（CBC/CTR 随机访问 + 密钥文件 + 授权信息/凭据加密）

> 状态：✅ 通过（2026-08-13，Spike 7/7）
> 目标：落地需求 10.4/10.6 的 SM4 加密要求——本地缓存、凭据、授权信息均不以明文存储；SM4 为大文件提供可随机访问的 CTR 模式（预览 Range / 断点续传兼容）。

## 1. 交付内容

- [Sm4Crypto](../../src/Station.Shared/Station.Infrastructure/Security/Sm4Crypto.cs)：
  - **SM4-CBC + PKCS7**：小数据（授权信息、凭据）加密，格式 `IV(16B)+密文`；
  - **SM4-CTR（自实现随机访问流）**：大文件缓存加密，格式 `nonce(16B)+密文`；`Sm4DecryptStream` 支持按明文偏移解密（块对齐），预览 Range/上传续传可直接定位；
  - `EncryptFile / EncryptInPlace / CreateDecryptReader`；
- [Sm4KeyProvider](../../src/Station.Shared/Station.Infrastructure/Security/Sm4KeyProvider.cs)：本机密钥文件（默认 `%LOCALAPPDATA%/Station/keys/sm4.key`），Windows 用 DPAPI 保护、Linux 0600，首次自动生成；
- [Sm4SecretProtector](../../src/Station.Shared/Station.Infrastructure/Security/Sm4SecretProtector.cs)：`sm4:` 前缀密文，无前缀原样返回（兼容未升级配置）；
- **授权信息加密存储**：`LicenseInfo.PayloadEnc` 保存授权文件原文的 SM4 密文，激活即加密落库；
- **凭据解密接线**：FTP/SFTP 密码、记录仪绑定 ini 密钥支持 `sm4:` 加密值（配置即密文，运行期解密）；
- DI 注册 `ISm4KeyProvider`。

## 2. 验证结果（Spike 7/7）

| 场景 | 结果 |
|---|---|
| SM4-CBC 往返一致，错误密钥拒绝 | ✅ |
| SM4-CTR 整读一致（1MB+37 字节） | ✅ |
| SM4-CTR 随机偏移切片一致（0/1/15/16/1000/65536/1000000） | ✅ |
| 密钥文件自动创建、两次读取一致 | ✅ |
| 凭据保护器：`sm4:` 前缀、解密往返、明文透传 | ✅ |
| `sm4:` 加密密码直连真实 SFTP 上传成功 | ✅ |
| 授权激活后 PayloadEnc 为密文（不含明文标记），解密一致，状态 Activated | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.Sm4 -c Release
```

## 3. 说明与后续（M40）

- CTR 密钥流使用加密方向生成（`E(counter)`），已修复初版解密方向错误；
- M40：缓存文件 SM4 加密接入采集/上传/预览全链路。
