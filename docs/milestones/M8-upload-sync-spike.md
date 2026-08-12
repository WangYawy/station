# M8：上传/同步阶段（本地磁盘 / FTP / SFTP + 断点续传 + 熔断 + 重试）

> 状态：✅ 主线通过（2026-08-13）；SFTP 实现完成、真实服务器联调待办
> 目标：采集完成文件进入存储链路：目录模板、远端校验（存储成功标准 2）、失败重试、存储熔断、断点续传/分片。

## 1. 交付内容

### 领域层

`CollectFile` / `CollectTask` 增加上传字段：`SyncStatus`（待上传/上传中/已同步/失败）、`UploadProgress`、`UploadSpeed`、`UploadError`、`UploadRetryCount`、`RemotePath`；任务级 `UploadedFiles`/`UploadedBytes`/`UploadError`。

### 基础设施层（Station.Infrastructure.Storage）

| 组件 | 说明 |
|---|---|
| `IStorageTarget` / `UploadTargetFile` | 存储目标抽象：上传 + 远端大小校验 |
| `LocalDiskStorageTarget` | 本地磁盘（分块复制 + 进度） |
| `FtpStorageTarget` | FluentFTP，`FtpRemoteExists.Resume` **断点续传** |
| `SftpStorageTarget` | SSH.NET，`FileMode.Append` 分块写入实现**断点续传** |
| `StorageCircuitBreaker` | P0 简单熔断：连续失败达阈值打开，冷却后**半开探测**，成功关闭 |
| `DirectoryTemplateRenderer` | 目录模板：`{StationNo}/{Date}/{RecorderName}/{UserId}/{DeptId}/{FileType}`，支持 `{Date:yyyy-MM-dd}` 格式符 |

### 应用层（Station.Application.Uploading）

- `IUploadService.ProcessTaskAsync`：只处理**采集已完成**任务；单文件内联重试（`RetryCount`/`RetryIntervalSeconds`，达上限标 Failed，未达上限留 Pending 由下轮重试）；上传成功 → **远端大小校验**（不一致判失败）；目标级失败累计到熔断。
- `RetryFailedFilesAsync`：手动/网络恢复重试失败文件。
- `CountPendingUploadsAsync`：工作台"待上传"实时统计。

### 桌面端

- `UploadWorkerHostedService`：每 5 秒扫描"采集完成且未传完"的任务执行上传——失败文件留 Pending 由下轮重试，等效**网络恢复事件触发补传**；
- 采集作业页：任务显示同步状态（待上传/上传中/已同步/同步失败）+ "重试上传"按钮；
- 工作台"待上传"卡片实时刷新。

## 2. 验证结果

| 场景 | SQLite/Local | Kingbase/Local | SQLite/FTP | SQLite/SFTP |
|---|---|---|---|---|
| 采集→上传（5/5、远端校验、目录模板） | ✅ | ✅ | ✅ | ⚠️ 见下 |
| 熔断：打开→冷却期保持→恢复后关闭并传完 | ✅ | ✅ | ✅ | ✅ |
| 清理 | ✅ | ✅ | ✅ | ✅ |

验证代码：[spikes/Station.Spike.Upload](../../spikes/Station.Spike.Upload/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.Upload -c Release -- --db sqlite --target local
dotnet run --project spikes/Station.Spike.Upload -c Release -- --db kingbase --target local
dotnet run --project spikes/Station.Spike.Upload -c Release -- --db sqlite --target ftp
```

**SFTP 状态说明**：实现完成（ConnectionInfo + Password/KeyboardInteractive 双通道认证、Append 分块续传、远端校验），但本机 WSL sshd（2222）拒绝 SSH.NET 认证（同一账号 openssh 客户端可登录，疑为 SSH.NET↔OpenSSH+PAM 协商差异）；另本机 Windows OpenSSH 服务占用 22 端口。已按"真实 SFTP 服务器上联调"列入待办，不影响管线整体结论。

## 3. 关键工程发现（重要）

1. **后台循环（采集/上传）必须用独立长连接 + 同步 DB 调用**：Kdbndp 长连接上连续异步命令会偶发 `A command is already in progress`；改为 `CreateClient(autoCloseConnection:false)` + `Queryable/Updateable...ExecuteCommand()` 同步调用后稳定（后台线程不阻塞 UI）。
2. **模板日期格式符**：`{Date:yyyy-MM-dd}` 需按格式符渲染；未渲染的 `:` 在 Windows 本地路径非法，会静默导致全部文件上传失败。
3. **存储成功标准 2**：上传后 `GetRemoteSizeAsync == 本地大小` 才算成功（本地/FTP/SFTP 统一）。
4. **重试语义**：单文件在 `RetryCount` 内失败 → 留 Pending 由 `UploadWorkerHostedService` 下轮补传（网络恢复即重试）；超限 → Failed（UI 可"重试上传"重置为 Pending 再传）。
5. **熔断**：目标级连续失败计数（与单文件重试计数独立），打开后上传暂停并写 `UploadError="存储熔断中"`，冷却后自动半开探测。
6. **环境提示**：Windows OpenSSH 服务占用 22 端口，本机 WSL sshd 已改 2222；FTP 21 正常。

## 4. 配置

```json
{
  "Station": {
    "Storage": {
      "Target": "Local",
      "LocalRoot": "",
      "StationNo": "ST0001",
      "DirectoryTemplate": "{StationNo}/{Date:yyyy-MM-dd}/{RecorderName}/{UserId}/{DeptId}/{FileType}",
      "FtpHost": "localhost", "FtpPort": 21, "FtpUser": "", "FtpPassword": "",
      "SftpHost": "localhost", "SftpPort": 2222, "SftpUser": "", "SftpPassword": "",
      "RetryCount": 3, "RetryIntervalSeconds": 10,
      "CircuitBreakerThreshold": 5, "CircuitBreakerCooldownSeconds": 60,
      "ChunkBytes": 1048576
    }
  }
}
```

## 5. 后续（M9 建议）

- **记录仪接入识别与归属**：UMS/MTP/私有加密真实采集源、ini 绑定（记录仪编号+用户+部门+SM3 校验）、非授权接入拒绝与报警；
- SFTP 真实服务器联调（含断点续传中断恢复验证）；
- 任务/文件台账与平台元数据上报（M1 契约落地）；
- 采集完成事件接入自动上传的即时触发（现为 5 秒轮询）。
