# M57：Serilog 结构化日志

> 状态：✅ 通过（2026-08-24，桌面/平台日志文件实测 + DesktopAudit 17/17、Settings 14/14、StationDetail 12/12 + 双端构建 + linux-x64 发布）
> 目标：两端接入 Serilog（控制台 + 按天滚动文件），并补全业务关键路径的日志输出。

## 1. 接入与配置

- 桌面端（Bootstrapper）：`Serilog.Extensions.Hosting` + Console/File sink；日志目录 `StationPaths.DataDirectory/logs/desktop-yyyyMMdd.log`（按天滚动、保留 30 天）；`UseSerilog(..., writeToProviders: true)` 保证 ILogger<T> 事件进 Serilog；appsettings.json 增加 `Serilog` 节（Microsoft/System 降为 Warning）；
- 平台端（Program.cs）：`Serilog.AspNetCore`（含请求日志中间件 `UseSerilogRequestLogging`）；日志目录 `AppContext.BaseDirectory/logs/platform-yyyyMMdd.log`；
- 共享 DI：`AddStationDatabase` 统一 `AddLogging()`，spike 自建容器也能解析 ILogger<T>；Application/Infrastructure 补 `Microsoft.Extensions.Logging.Abstractions 8.0.3`（对齐 SSH.NET/PDFsharp 依赖）。

## 2. 补全日志位置

### 共享应用层

- 采集任务（创建/扫描/暂停/恢复/取消/结束/异常/紧急标记）；
- 上传（成功/失败重试/熔断打开）；
- 报警写入、授权检查（到期/时钟回拨）、授权激活；
- 登录（成功/失败/锁定）、记录仪识别（未绑定/篡改/非授权/已绑定）；
- 配置应用（成功/失败）、系统设置更新（含操作人）、缓存清理汇总；
- 远程指令（签名校验失败/执行结果）、平台上报失败重试。

### 后台 Worker / 基础设施

- 平台同步轮询失败、上传/台账轮询异常、定时采集失败、记录仪接入识别/自动采集、数据库初始化完成、本地/平台备份完成与失败、设备自检汇总。

### 平台 Web

- `UseSerilogRequestLogging`：每次 HTTP 请求（方法/路径/状态码/耗时）。

## 3. 验证

| 场景 | 结果 |
|---|---|
| 桌面端日志文件生成（宿主生命周期/数据库初始化/设置更新落盘） | ✅ |
| 平台端日志文件生成 + 请求日志（GET/POST/401/403/200） | ✅ |
| DesktopAudit 17/17、Settings 14/14、StationDetail 12/12 回归 | ✅ |
| 双端全量构建 + linux-x64 单文件发布 | ✅ |

## 4. 说明

- 桌面端日志位于用户数据目录（`%LOCALAPPDATA%\Station\logs` / `~/.local/share/Station/logs`），部署手册已同步；
- 平台端日志位于程序目录 `logs/`；敏感信息（密码/授权文件内容）不入日志。
