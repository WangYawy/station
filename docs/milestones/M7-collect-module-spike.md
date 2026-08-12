# M7：采集作业模块（记录仪接入→扫描筛选→复制校验→跳过/中断/暂停/擦除）

> 状态：✅ 通过（2026-08-12，四库 Spike 7/7 + 桌面端冒烟）
> 目标：打通产品核心链路"记录仪接入即采集"，实现任务状态机与 UI 操作台；无硬件阶段用"模拟记录仪"采集源开发验证。

## 1. 交付内容

### 领域层（Station.Domain）

| 实体 | 表名 | 说明 |
|---|---|---|
| `CollectTask` | `station_collect_task` | 一次连接一个任务：任务号、记录仪、协议、状态、进度/速度/字节、操作人、结果 |
| `CollectFile` | `station_collect_file` | 任务内文件：元数据、指纹、采集状态、进度、速度、错误 |

状态机：`CollectTaskStatus`（已创建/扫描中/采集中/已暂停/已完成/已中断/失败/已取消）；`CollectFileStatus`（待采集/采集中/校验中/已完成/已跳过/失败/异常/已取消）。

### 应用层（Station.Application.Collecting）

| 组件 | 说明 |
|---|---|
| `CollectOptions` | 采集策略（`Station:Collect`）：缓存目录、自动采集、跳过已采集、完成后擦除（默认关）、分块、扩展名白名单 |
| `ICollectSource` | 采集源抽象（UMS/MTP/私有加密统一接口）：扫描/复制/擦除 |
| `SimulatedCollectSource` | 模拟记录仪（开发验证）：生成样例文件、分块模拟进度/速度、可擦除 |
| `ICollectTaskService` | 接入→扫描筛选→复制校验→跳过已采集→中断/暂停/取消→完成后擦除 |

关键行为：
- **接入即采**：`AutoCollectOnConnect` 默认开；手动任务可单独 Start；
- **已采集跳过**：指纹 = 文件名\|大小\|修改时间，已 Completed 的自动 Skipped；
- **断线/拔出**：任务 Interrupted；进行中文件→Abnormal，未开始文件→Canceled（需求：未上传文件标异常或取消）；
- **暂停/恢复**：当前文件回 Pending，其余保持，Resume 继续；
- **完成后擦除**：默认关闭；开启时任务完成后擦除源文件，失败不阻塞任务（记录到任务备注，报警 P1）；
- **单文件失败**：标记 Failed 并继续其余文件。

### UI 层（Station.Desktop.UI）

- `CollectModuleView`（采集作业）：任务列表（状态/进度/速度/自动-手动）、文件明细（进度条）、操作按钮：模拟接入/开始/暂停/恢复/取消/模拟拔出；
- 工作台"采集队列"改为绑定活动任务实时刷新（原静态占位替换）。

## 2. 验证结果（4 库 × 7 项）

| 验证项 | SQLite | Kingbase | MySQL | PostgreSQL |
|---|---|---|---|---|
| 自动采集（接入即采，5/5，字节一致） | ✅ | ✅ | ✅ | ✅ |
| 已采集文件自动跳过（5/5） | ✅ | ✅ | ✅ | ✅ |
| 中途拔线中断（Interrupted+异常/取消文件） | ✅ | ✅ | ✅ | ✅ |
| 暂停/恢复（Paused→Completed 5/5） | ✅ | ✅ | ✅ | ✅ |
| 取消（Canceled） | ✅ | ✅ | ✅ | ✅ |
| 完成后擦除（源文件清零） | ✅ | ✅ | ✅ | ✅ |
| 清理 | ✅ | ✅ | ✅ | ✅ |

验证代码：[spikes/Station.Spike.Collect](../../spikes/Station.Spike.Collect/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.Collect -c Release -- --db sqlite
dotnet run --project spikes/Station.Spike.Collect -c Release -- --db kingbase
```

桌面端冒烟：启动后"采集作业"页点"模拟接入记录仪"→ 自动采集 → 任务/文件进度实时刷新 → 模拟拔出看中断；工作台队列同步显示活动任务。

## 3. 关键工程发现（必须沉淀）

1. **后台采集必须用独立长连接客户端**：采集循环与 UI/查询共用 SqlSugarScope 时，Kdbndp 出现 `Unknown message code: 0`/`Exception while reading from stream`（连接池并发复用冲突）。方案：`ISqlSugarFactory.CreateClient(options, autoCloseConnection: false)`，循环内单连接跑完整任务，结束后释放。
2. **进度落库节流**：每分块写一次库（100+ 次/任务）在高频下不稳定；按 **≥10% 增量**持久化、失败不中断复制，完成时最终落库。
3. **状态机默认值**：`TaskControl.FinalStatus` 默认必须是 `Completed`，否则正常完成被误标 Canceled（显式取消/中断才覆盖）。
4. **中断语义**：取消/中断后按"请求的终态"标记任务（Interrupted/Canceled），不能因无 Pending 文件而回退 Completed。
5. **收尾异常兜底**：Finalize 失败时用共享仓储把任务标 Failed 并记录原因，避免任务永久停在 Collecting 且无未观察异常。
6. 模拟源分块延迟（默认 15ms）用于演示可见进度与可靠的暂停/中断测试；真机 UMS/MTP 接入时替换 `ICollectSource` 实现即可，业务层不变。

## 4. 配置

```json
{
  "Station": {
    "Collect": {
      "CacheDirectory": "",              // 默认 %LOCALAPPDATA%/Station/collect-cache
      "AutoCollectOnConnect": true,
      "SkipCollected": true,
      "EraseAfterComplete": false,
      "ChunkBytes": 1048576,
      "SimulatedFileCount": 5,
      "SimulatedChunkDelayMs": 15
    }
  }
}
```

## 5. 后续（M8 建议）

- **上传/同步阶段**：任务完成后进入存储链路（本地磁盘/FTP/SFTP，断点续传/分片、存储熔断、失败重试），任务增加"同步状态"（待上传/同步中/已同步/失败）；
- **真实采集源**：UMS（DriveInfo 枚举+文件复制）、MTP（WPD）、私有加密 SDK 转 U 盘；
- 记录仪接入识别与归属（ini 绑定）接入任务创建（操作人/部门继承）；
- 擦除策略按部门配置、失败重试与报警。
