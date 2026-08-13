# M16：授权与机器指纹（离线激活 / 硬件绑定 / 到期立即中断采集）

> 状态：✅ 通过（2026-08-13，SQLite + Kingbase 7/7）
> 依据需求：P0 离线授权（不支持在线激活）；机器指纹=CPU+主板+磁盘+MAC，换硬件即新指纹；一个授权绑定一台；到期瞬间正在采集立即中断。

## 1. 交付内容

### 领域层

- `LicenseInfo`（`station_license`）：本地授权记录（授权号唯一、绑定指纹、有效期、状态）。

### 基础设施（Station.Infrastructure.Licensing）

- `WindowsMachineFingerprintProvider`：WMI 采集 CPU 序列号 / 主板序列号 / 磁盘序列号 + 网卡 MAC，组合后 **SM3** 哈希（换硬件即新指纹；Linux/信创实现后续补充）；
- `Sm3Checksum.ComputeHmac`：HMAC-SM3（授权文件签名）。

### 应用层（Station.Application.Licensing）

| 组件 | 说明 |
|---|---|
| `LicenseFileCodec` | 授权文件（station.lic）编解码 + 规范化签名/验签（HMAC-SM3，密钥 `Station:License:SigningKey`） |
| `LicenseGenerator` | 内部工具/测试用：输入 站点编号+指纹+到期时间 → 签名授权文件（正式交付由内部工具持有密钥生成） |
| `LicenseService` | 离线激活（验签 + **硬件指纹匹配** + 有效期校验，新激活使旧授权全部失效）、状态检查（Trial/Activated/Locked）、`IsValidNowAsync` |
| 采集接入 | `CollectTaskService.StartAsync` 前校验；**采集循环内每文件前校验，到期立即中断任务（Interrupted，未完成文件异常/取消）** |

### UI

- 顶部授权徽标动态显示：正式版·剩余X天 / 试用·剩余X天 / 授权已到期（每 60 秒刷新）。

## 2. 验证结果（SQLite + Kingbase × 7 项）

| 验证项 | SQLite | Kingbase |
|---|---|---|
| 机器指纹采集（本机 WMI，SM3 64 位） | ✅ | ✅ |
| 内部工具生成 + 激活（正式版 29 天） | ✅ | ✅ |
| 篡改签名拒绝 | ✅ | ✅ |
| 换硬件（指纹不匹配）拒绝 | ✅ | ✅ |
| 到期 → Locked + 采集被拒 | ✅ | ✅ |
| 采集进行中授权到期 → 任务 Interrupted + 文件异常/取消 | ✅ | ✅ |
| 清理 | ✅ | ✅ |

验证代码：[spikes/Station.Spike.License](../../spikes/Station.Spike.License/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.License -c Release -- --db sqlite
dotnet run --project spikes/Station.Spike.License -c Release -- --db kingbase
```

## 3. 配置

```json
{
  "Station": {
    "License": {
      "SigningKey": "station-license-signing-key-v1",
      "TrialDays": 30,
      "ProductCode": "STATION-DESKTOP-1"
    }
  }
}
```

## 4. 说明与后续（M17 建议）

1. 授权签名当前为 HMAC-SM3（对称密钥）；生产建议升级 **SM2 非对称**（内部工具私钥签名、采集站公钥验签）；
2. 授权状态/到期时间建议随平台心跳上报（平台侧 LicenseStatus 已就绪）；
3. 真实 UMS/MTP 采集源 + SFTP 真实服务器联调收尾；
4. 平台前端 Vue 工程化。
