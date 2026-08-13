# M33：SFTP 真实服务器联调 + UMS 采集源（真机就绪）

> 状态：✅ 通过（2026-08-13，SftpLive 5/5 + UmsSource 6/6；M8 全管线 SFTP 5/5+远端校验）
> 目标：打通"上传链路对真实 SFTP 服务器"的联调，并让采集侧具备真实 UMS（U 盘模式）采集能力，真机插入即可使用。

## 1. 交付内容

**SFTP 真实服务器联调**

- 环境：WSL OpenSSH 监听 `127.0.0.1:2222`（账号 `kingbase`），SSH.NET 密码/键盘交互认证均连通（此前"SSH.NET 被拒"的阻塞已解除）；
- 修复 [SftpStorageTarget](../../src/Station.Shared/Station.Infrastructure/Storage/SftpStorageTarget.cs) 两个真问题：
  - **尾块丢失**：`SftpFileStream` 在 Dispose 前未显式 `Flush()`，1MB 文件会少传 ~928 字节 → 增加显式 Flush（上传大小校验精确命中）；
  - **远端根目录**：相对模板路径会落到 `/ST0001/...` 根目录导致权限拒绝 → 新增 `StorageOptions.SftpRoot`，为空时默认使用登录用户主目录（SSH.NET `WorkingDirectory`），可配置覆盖；
- 端到端验证（[SftpLive spike](../../spikes/Station.Spike.SftpLive/Program.cs) 5/5）：上传→远端大小校验→同一路径追加续传（FileMode.Append 模拟断点续传）→远端清理；
- 全管线（[M8 Upload spike](../../spikes/Station.Spike.Upload/Program.cs)，target=sftp）：采集→上传 5/5、远端校验 True、模板路径正确（`ST0001/日期/记录仪/...`）；同步修了该 spike 的过时问题（license 等新表、残留 sqlite、远端测试目录自动清理）。

**UMS 采集源（真机就绪）**

- 新增 [UmsCollectSource](../../src/Station.Shared/Station.Application/Collecting/UmsCollectSource.cs)：枚举可移动磁盘作为记录仪根目录，扫描/分块复制/普通删除擦除与模拟源同一套接口；
- `CollectOptions.SourceMode`（`simulated` 默认 / `ums`）+ `UmsRootOverride`（开发/测试用目录覆盖，与真机同一代码路径）；
- 无设备且未配置覆盖目录时抛出明确错误"未检测到 UMS 设备"；
- DI 按 `SourceMode` 选择采集源；验证（[UmsSource spike](../../spikes/Station.Spike.UmsSource/Program.cs) 6/6）：扫描/根目录/复制/擦除/无设备报错/DI 切换。

## 2. 验证结果

| 场景 | 结果 |
|---|---|
| SSH.NET 密码 / 键盘交互认证连接真实 OpenSSH | ✅ |
| SFTP 上传 1MB → 远端大小精确一致（修复 Flush 后） | ✅ |
| SFTP 同一路径追加续传（1MB + 512KB）→ 远端 1.5MB | ✅ |
| SFTP 远端目录清理 | ✅ |
| 全管线采集→SFTP 上传 5/5 + 远端大小校验 + 模板路径正确 | ✅ |
| UMS 扫描 / 根目录解析 / 复制 / 擦除（覆盖目录同真机路径） | ✅ |
| UMS 无设备明确报错；DI 按 SourceMode 切换（ums/simulated） | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.SftpLive -c Release
dotnet run --project spikes/Station.Spike.UmsSource -c Release
dotnet run --project spikes/Station.Spike.Upload -c Release -- --db sqlite --target sftp
```

## 3. 真机接入说明与后续

- 真机接入：插入 U 盘/记录仪后，配置 `Station:Collect:SourceMode=ums` 即走真实 UMS 采集（无设备时采集任务给出明确提示）；生产上默认不开启 UMS，避免误采；
- MTP / 私有加密（SDK 转 UMS）采集源：UMS 已覆盖"私有加密 SDK 转 U 盘模式"，MTP 需 Windows 便携设备 API，列 P1；
- 已知遗留：M8 spike 在 SQLite 下存在偶发并发抖动（采集后台与 spike 轮询共用单文件库），与 SFTP/UMS 无关，SFTP 端到端以 SftpLive spike 为准；
- 后续交付准备：桌面端单文件发布、平台 Docker Compose、信创 x86_64/ARM64 构建验证。
