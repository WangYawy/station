# Station 采集站

> 记录仪采集、加密存储、审计溯源、平台协同的一体化采集站系统。

------

## 一、项目简介

Station 是一款面向记录仪（UMS / MTP / 私有 SDK）的**采集站系统**，支持单机自治与平台协同两种模式，适用于 30 路 USB 并发接入的桌面场景。

核心能力：

- **设备识别**：绑定文件（station_bind.ini）+ HMAC-SM3 签名 + 台账三方校验
- **文件采集**：多协议接入、明文摘要、元数据签名、就地加密、断点暂停/恢复
- **文件上传**：本地 / FTP / SFTP 多目标，分片、重试、存储熔断，明文/密文可配
- **平台协同**：配置快照拉取、报警上报、指令回执、断网补报
- **授权与审计**：离线激活、时钟回拨检测、全链路审计日志、归档保留

------

## 二、核心特性





| 能力          | 说明                                                         |
| :------------ | :----------------------------------------------------------- |
| 充血领域模型  | 聚合根封装状态机与不变量，杜绝贫血模型                       |
| 领域事件      | 关键状态变化通过 `IDomainEventBus` 广播，支持 Sync / Channel 双模式 |
| CQRS 风格     | 写走 `IRepository` / `I*Service`，读走 `I*QueryService`，DTO 直投影 |
| 双模型隔离    | 领域聚合与持久化 PO 分离，Domain 层零 ORM 依赖               |
| 短/长连接仓储 | 事务操作用短连接 UoW，后台长任务用长连接 Sync 仓储           |
| 30 路并发优化 | 聚合内进度节流 + `ProgressAggregator` 合并 + 原子 UPDATE     |
| 国密支持      | SM2 / SM3 / SM4，算法策略可配置、可回退                      |
| UI 通知解耦   | UI 事件与领域事件分离，界面刷新零延迟                        |

------

## 三、技术栈

- **运行时**：.NET 8 / C#
- **ORM**：SqlSugar（短连接 + 长连接双客户端）
- **数据库**：金仓 KingbaseES（Kdbndp 驱动）
- **桌面端**：Avalonia + CommunityToolkit.Mvvm
- **加密**：国密 SM2 / SM3 / SM4
- **ID 生成**：雪花算法（`IIdGenerator`）
- **依赖注入**：Microsoft.Extensions.DependencyInjection
- **日志**：Microsoft.Extensions.Logging

------

## 四、项目结构

text

```
Station.sln
├── Station.Domain                  领域层（纯 C#，零 ORM 依赖）
│   ├── Accounts/                   账号聚合 + 仓储契约
│   ├── Departments/                部门
│   ├── Users/                      用户 + 用户角色
│   ├── Roles/                      角色 + 角色权限
│   ├── Permissions/                权限点
│   ├── Recorders/                  记录仪台账
│   ├── Alerts/                     报警
│   ├── Collecting/                 采集任务 + 采集文件（聚合 + 领域事件）
│   ├── UploadedFiles/              已上传文件台账（POCO）
│   ├── Licensing/                  授权
│   ├── Outbox/                     上报队列
│   ├── Stations/ Clocks/           站点信息 / 时钟状态（配置类 POCO）
│   ├── Cryptography/ Settings/     算法策略 / 键值配置
│   ├── Auditing/                   审计日志（POCO）
│   ├── Audit/                      操作类型词表
│   └── Abstractions/               AggregateRoot / IDomainEvent / IDomainEventBus
│
├── Station.Data                    抽象数据契约（框架级，不引用 Domain）
│   ├── Repositories/               IRepository<T> / ILoopRepository<T> / IUnitOfWork
│   ├── Paging/                     PageQuery / PageResult
│   ├── SoftDelete/                 ISoftDelete
│   └── IdGeneration/               IIdGenerator
│
├── Station.Data.SqlSugar           SqlSugar 通用实现（框架级）
│   ├── Repositories/               RepositoryBase / LoopRepositoryBase
│   └── Client/                     ISqlSugarClient / ILoopSqlSugarClient
│
├── Station.Contracts               跨端契约（DTO / 枚举 / 常量）
│
├── Station.Application             应用层（用例编排）
│   ├── Accounts/ Authentication/   账号服务 / 认证服务
│   ├── Users/ Departments/ Roles/  用户域应用服务
│   ├── Collecting/ Uploading/      采集与上传应用服务
│   ├── Recorders/ Alerts/          记录仪 / 报警
│   ├── PlatformSync/               平台同步（配置应用 / 指令执行 / Outbox）
│   ├── Audit/                      审计应用服务
│   ├── Common/Notifications/       UI 通知接口
│   └── */Queries/                  读侧 QueryService 契约
│
├── Station.Infrastructure          基础设施（PO / Mapper / Repository / DI）
│   ├── Persistence/                按模块分组的持久化实现
│   │   ├── Accounts/ Departments/ Users/ Roles/ Permissions/
│   │   ├── Collecting/ Recorders/ Alerts/ Auditing/
│   │   ├── Stations/ Clocks/ Cryptography/ Settings/ Licensing/ Outbox/
│   │   └── UploadedFiles/
│   ├── Events/                     事件总线实现 + ProgressAggregator
│   ├── DeviceDetection/            设备监听（RecorderConnectMonitor）
│   └── DependencyInjection.cs
│
├── Station.Crypto                  加密算法实现（国密）
├── Station.Desktop                 Avalonia 桌面端
└── Station.Engine                  采集引擎（协议适配、文件系统）
```

<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 12 12" fill="none" class="_9bc997d _33882ae"><path d="M-5.24537e-07 0C-2.34843e-07 6.62742 5.37258 12 12 12L0 12L-5.24537e-07 0Z" fill="currentColor"></path></svg>

<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 12 12" fill="none" class="_9bc997d _28d7e84"><path d="M-5.24537e-07 0C-2.34843e-07 6.62742 5.37258 12 12 12L0 12L-5.24537e-07 0Z" fill="currentColor"></path></svg>

------

## 五、架构设计

### 5.1 依赖方向

text

```
Desktop / Engine
      │
      ▼
Application  ──▶ Domain ◀── Contracts
      │             ▲
      ▼             │
Infrastructure ─────┘
      │
      ▼
Data.SqlSugar ──▶ Data
```

<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 12 12" fill="none" class="_9bc997d _33882ae"><path d="M-5.24537e-07 0C-2.34843e-07 6.62742 5.37258 12 12 12L0 12L-5.24537e-07 0Z" fill="currentColor"></path></svg>

<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 12 12" fill="none" class="_9bc997d _28d7e84"><path d="M-5.24537e-07 0C-2.34843e-07 6.62742 5.37258 12 12 12L0 12L-5.24537e-07 0Z" fill="currentColor"></path></svg>

**规则**：

- `Domain` 不引用任何 ORM / 基础设施
- `Data` / `Data.SqlSugar` 是**框架级共享库**，不引用 `Domain`
- `Application` 只依赖 `Domain` + `Data` 契约
- `Infrastructure` 是唯一同时认识 `Domain` 和 `SqlSugar` 的层
- 应用服务不直接调用 SqlSugar，全部通过仓储 / QueryService

### 5.2 写读分离（CQRS 风格）





| 场景                      | 使用                                 |
| :------------------------ | :----------------------------------- |
| 加载聚合准备修改          | `IXxxRepository`（短连接 UoW）       |
| 后台长任务批量操作        | `IXxxSyncRepository`（长连接）       |
| 写前存在性校验            | `IXxxQueryService.ExistsAsync(...)`  |
| 列表 / 详情 / 下拉 / 字典 | `IXxxQueryService`（DTO 直投影）     |
| 写完之后返回展示数据      | Controller 重新调 `IXxxQueryService` |

### 5.3 领域事件

- 聚合根通过 `Raise(IDomainEvent)` 注册事件
- 应用服务持久化后通过 `IDomainEventBus.PublishRangeAsync(...)` 发布
- 事件总线支持三种模式：
  - **Sync**：同步立即派发（开发调试）
  - **Channel**：内存有界 Channel + 后台 Dispatcher 批量派发（生产推荐）
  - **Outbox**：持久化 outbox 表（阶段二）
- 事件处理器通过 `IDomainEventHandler<T>` 注册，DI 自动解析

### 5.4 采集上传流程

text

```
USB 插入
   │
   ▼
RecorderConnectMonitor（轮询 3s，稳定 2 次判定接入）
   │
   ▼
RecorderIdentificationService（4 层校验）
   0. 总开关
   1. 读 station_bind.ini
   2. HMAC-SM3 验签
   3. 台账校验（Recorder / User / Dept 在册）
   4. 用户/部门一致性
   │
   ▼
CollectTaskService.CreateTaskAsync
   │
   ▼
StartAsync → 扫描 → 批量插入 CollectFile
   │
   ▼
后台采集线程（长连接、专用线程）
   BeginCopy → CopyAsync（进度回调）
   → BeginVerify → 摘要 / 签名 / 加密
   → CompleteCollecting → 领域事件
   │
   ▼
ProgressAggregator（每 200ms 合并进度）
   │
   ▼
UploadService.ProcessTaskAsync（任务级上传循环）
   BeginUpload → UploadFileAsync → CompleteUpload
   │
   ▼
WriteUploadedFileAsync（幂等写 station_uploaded_file）
   │
   ▼
SyncOutboxService.EnqueueAsync（上报平台）
```

<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 12 12" fill="none" class="_9bc997d _33882ae"><path d="M-5.24537e-07 0C-2.34843e-07 6.62742 5.37258 12 12 12L0 12L-5.24537e-07 0Z" fill="currentColor"></path></svg>

<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 12 12" fill="none" class="_9bc997d _28d7e84"><path d="M-5.24537e-07 0C-2.34843e-07 6.62742 5.37258 12 12 12L0 12L-5.24537e-07 0Z" fill="currentColor"></path></svg>

### 5.5 性能设计





| 挑战               | 方案                                                         |
| :----------------- | :----------------------------------------------------------- |
| 30 路 USB 进度事件 | 聚合内节流（10% 档），UI 通过 `IUiNotificationService` 直发  |
| Task 行锁竞争      | `IncrementProgress` 原子 UPDATE + `ProgressAggregator` 合并 200ms |
| 事件总线阻塞       | 有界 Channel + `TryWrite` 非阻塞 + 后台批量派发              |
| 后台线程饥饿       | `TaskCreationOptions.LongRunning` 专用线程                   |
| 长连接 Kdbndp 限制 | 长连接仓储全同步 API，禁止同步异步交叉调用                   |

------

## 六、快速开始

### 6.1 环境要求

- .NET 8 SDK
- KingbaseES（金仓）数据库，或兼容 SqlSugar 的数据库
- Visual Studio 2022 / Rider / VS Code

### 6.2 构建与运行

bash

```
git clone <repo-url>
cd Station
dotnet restore
dotnet build
dotnet run --project Station.Desktop
```

<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 12 12" fill="none" class="_9bc997d _33882ae"><path d="M-5.24537e-07 0C-2.34843e-07 6.62742 5.37258 12 12 12L0 12L-5.24537e-07 0Z" fill="currentColor"></path></svg>

<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 12 12" fill="none" class="_9bc997d _28d7e84"><path d="M-5.24537e-07 0C-2.34843e-07 6.62742 5.37258 12 12 12L0 12L-5.24537e-07 0Z" fill="currentColor"></path></svg>

首次启动：

1. `StationDbInitializerHostedService` 自动建表（SqlSugar CodeFirst）
2. `AuthSeeder` 播种默认管理员账号：`admin / Admin@123`
3. 数据库初始化完成后进入桌面端登录界面

### 6.3 关键配置

`appsettings.json`：

json

```
{
  "Station": {
    "ConnectionStrings": {
      "Default": "Server=...;Database=...;"
    },
    "Events": {
      "Mode": "Channel",
      "ChannelCapacity": 10000,
      "BatchSize": 200,
      "FlushIntervalMs": 100,
      "ProgressFlushIntervalMs": 200,
      "RetentionDays": 30,
      "OverflowPolicy": "DropOldest"
    },
    "Auth": {
      "DefaultAdminUserName": "admin",
      "DefaultAdminPassword": "Admin@123",
      "MaxFailedAttempts": 5,
      "LockoutMinutes": 15
    },
    "Binding": {
      "EnableBinding": true,
      "EnableSignature": true,
      "EnableBindingValidation": true
    },
    "Collect": {
      "AutoCollectOnConnect": true,
      "SkipCollected": true,
      "EncryptCache": true,
      "EraseAfterComplete": false,
      "MaxEmergencyTasks": 3,
      "CacheDirectory": "D:\\Station\\Cache"
    },
    "Storage": {
      "Target": "Local",
      "DirectoryTemplate": "{station}/{yyyy}/{MM}/{dd}/{recorder}/{user}/{ext}",
      "RetryCount": 3,
      "RemoteContentMode": "Plaintext",
      "EnableFileSignature": true
    }
  }
}
```

<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 12 12" fill="none" class="_9bc997d _33882ae"><path d="M-5.24537e-07 0C-2.34843e-07 6.62742 5.37258 12 12 12L0 12L-5.24537e-07 0Z" fill="currentColor"></path></svg>

<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 12 12" fill="none" class="_9bc997d _28d7e84"><path d="M-5.24537e-07 0C-2.34843e-07 6.62742 5.37258 12 12 12L0 12L-5.24537e-07 0Z" fill="currentColor"></path></svg>

------

## 七、开发规范

### 7.1 命名约定





| 分类         | 规范                 | 示例                          |
| :----------- | :------------------- | :---------------------------- |
| 领域模型     | 无前缀名词           | `Account` / `CollectTask`     |
| 持久化模型   | `XxxPo`              | `AccountPo` / `CollectTaskPo` |
| 写输入       | `XxxCommand`         | `CreateUserCommand`           |
| 读输出       | `XxxDto`             | `UserListItemDto`             |
| 应用服务接口 | `IXxxService`        | `IUserService`                |
| 查询服务接口 | `IXxxQueryService`   | `IUserQueryService`           |
| 仓储接口     | `IXxxRepository`     | `IUserRepository`             |
| 长连接仓储   | `IXxxSyncRepository` | `IUserSyncRepository`         |
| 领域事件     | 过去式               | `CollectTaskCompleted`        |

### 7.2 分层规则

**必须遵守**：

1. **Domain 层零 ORM**：不允许 `using SqlSugar`，不允许特性
2. **Application 不引用 Infrastructure**：只依赖 Domain 契约 + Application 接口
3. **写服务不返回读数据**：写方法返回 `OpResult` / `long Id`，需要展示时由 Controller 走 QueryService
4. **聚合内发事件**：应用服务不手工构造领域事件
5. **Id 由 IIdGenerator 生成**：不依赖数据库自增

### 7.3 异步约定





| 层                   | 执行模式                            | 原因                                                  |
| :------------------- | :---------------------------------- | :---------------------------------------------------- |
| 应用服务（对外 API） | `async Task` + `await`              | 短连接 SqlSugar 异步稳定                              |
| 后台主循环           | `async Task` + 仅在文件 I/O `await` | 长连接 Kdbndp 同步；文件 I/O 异步                     |
| 长连接仓储           | **全同步**                          | Kdbndp 同步异步交叉会报 `command already in progress` |
| 事件总线             | `await` 统一                        | 实现内部返回 `Task.CompletedTask` 无成本              |

### 7.4 事件使用原则

- **关键状态变化** → 领域事件（`IDomainEventBus`），异步、驱动审计 / 同步 / 聚合
- **UI 界面刷新** → UI 通知（`IUiNotificationService`），同步、即时、零延迟
- **高频进度** → 聚合内节流 + 直接调用 UI 通知，不进事件总线

------

## 八、数据库表清单





| 表名                        | 用途         | 类型     |
| :-------------------------- | :----------- | :------- |
| `station_account`           | 登录账号     | 聚合根   |
| `station_user`              | 用户（人员） | 聚合根   |
| `station_dept`              | 部门         | 聚合根   |
| `station_role`              | 角色         | 聚合根   |
| `station_permission`        | 权限点       | 聚合根   |
| `station_user_role`         | 用户角色关联 | 关联表   |
| `station_role_permission`   | 角色权限关联 | 关联表   |
| `station_recorder`          | 记录仪台账   | 聚合根   |
| `station_alert`             | 报警         | 聚合根   |
| `station_collect_task`      | 采集任务     | 聚合根   |
| `station_collect_file`      | 采集文件     | 聚合根   |
| `station_uploaded_file`     | 已上传台账   | 流水     |
| `station_license`           | 授权         | 聚合根   |
| `station_sync_outbox`       | 上报队列     | 聚合根   |
| `station_audit_log`         | 审计日志     | 流水     |
| `station_audit_log_archive` | 审计归档     | 流水     |
| `station_info`              | 站点信息     | 单行配置 |
| `station_clock_state`       | 时钟状态     | 单行配置 |
| `station_crypto_policy`     | 算法策略     | 配置     |
| `station_sys_setting`       | 键值配置     | 配置     |

------

## 九、扩展指引

### 新增聚合根

1. Domain 层新建 `Xxx`（继承 `AggregateRoot`）、`IXxxRepository`、`IXxxSyncRepository`
2. Infrastructure 层新建 `XxxPo` / `XxxMapper` / `XxxRepository` / `XxxSyncRepository`
3. Application 层新建 `IXxxService` / `XxxService` / `IXxxQueryService` / DTO
4. `DependencyInjection.cs` 注册
5. `StationDbInitializerHostedService.EnsureCreated` 加入 `typeof(XxxPo)`
6. 存量库补审计列（如需要）

### 新增领域事件

1. `Station.Domain/.../Events/XxxEvent.cs` 实现 `IDomainEvent`
2. 聚合内 `Raise(...)` 注册
3. 应用服务持久化后 `PublishRangeAsync(...)`
4. 事件处理器 `IDomainEventHandler<XxxEvent>` 注册到 DI