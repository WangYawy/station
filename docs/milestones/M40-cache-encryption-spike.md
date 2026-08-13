# M40：缓存文件 SM4 加密全链路（采集加密 → 台账SM3 → 上传解密 → 预览Range）

> 状态：✅ 通过（2026-08-13，Spike 4/4 + M8 回归 3/3）
> 目标：需求 10.4"文件缓存使用 SM4 加密"落地——采集落缓存即加密，台账 SM3 与上传/预览均基于解密后的明文，且不影响断点续传与 Range 预览。

## 1. 交付内容

- `CollectOptions.EncryptCache`（默认开启）：采集文件复制到本地缓存后**原地 SM4 加密**（`EncryptInPlace`，位于大小校验之后、置完成之前，半截文件仍作废删除）；
- **台账 SM3**：`FileLedgerService` 对加密缓存用 `Sm4DecryptStream` 计算明文 SM3（采集完整性校验值保持为明文哈希）；
- **上传解密**：`UploadTargetFile` 增加 `LocalStreamFactory`，`UploadService` 在启用加密时提供解密流；本地/FTP/SFTP 三类存储目标统一走流工厂读取明文；
- **预览 Range 解密**：`FileStreamController` 在启用加密时按 Range 偏移创建解密流（206/200 + Content-Range），未启用保持原 `PhysicalFile` 路径；
- 旧版明文缓存兼容：`EncryptCache=false` 走原逻辑（M8 上传 spike 回归通过）。

## 2. 验证结果（Spike 4/4）

| 场景 | 结果 |
|---|---|
| 缓存文件已加密：大小 = 明文 + 16B，首块非明文 | ✅ |
| 台账 SM3 = 源文件明文 SM3 | ✅ |
| 上传 2/2，远端文件字节与源明文完全一致（解密后传输） | ✅ |
| 预览 Range（bytes=100-）→ 206，返回明文切片一致 | ✅ |
| M8 上传 spike 回归（EncryptCache=false）3/3 | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.CacheEncryption -c Release
```

## 3. 说明与后续

- **升级注意**：默认开启加密后，存量明文缓存文件不会自动迁移；已部署环境建议先以 `EncryptCache=false` 完成存量上传/清理，再开启加密；
- 密钥文件更换（如硬件/授权重置）会导致旧加密缓存不可读，属预期（缓存为待上传中间态）；
- 剩余 P0 缺口见基线差异清单：本地库每日备份、定时采集、任务暂停/恢复、缓存保留天数清理、远端 SM3 二次校验、时钟回拨检测、桌面端报警声音弹窗、状态监控页。
