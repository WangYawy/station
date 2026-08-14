# 视音频数据采集站与监控管理平台

面向执法记录仪等视音频设备的数据采集、存储、查看、监管和基础运维产品，包含三个子系统：

| 子系统 | 说明 | 解决方案 |
| :--- | :--- | :--- |
| 采集站桌面端 | Avalonia 跨平台桌面应用，含单机版内置 Web（自托管 + Controller 分层） | `Station.Desktop.sln` |
| 监控管理平台 | ASP.NET Core WebAPI + Vue 3，部署于客户环境（三库适配） | `Station.Platform.sln` |
| 单机版内置 Web | 随桌面端交付，默认本机访问，可配置局域网访问 | 随桌面端构建 |

## 仓库结构

```
docs/                         基线文档与原型
src/
├── Station.Shared/           双向共享：领域模型 + 通信契约
│   ├── Station.Contracts/    上报/同步/指令契约、枚举、ini 绑定格式
│   └── Station.Domain/       公共核心实体
├── Station.Desktop/          采集站桌面端（含单机版内置 Web）
└── Station.Platform/         监控管理平台
archive/                      历史代码与旧版文档（仅参考，不参与构建）
```

## 构建

```powershell
# 桌面端（含单机版内置 Web）
dotnet build .\Station.Desktop.sln -c Release

# 平台端
dotnet build .\Station.Platform.sln -c Release
```

依赖方向：`Desktop → Shared`、`Platform → Shared`；`Shared` 不依赖任何一端，`Platform` 永不引用 `Desktop`。

## 文档

- [开发基线需求规格说明书](docs/01视音频数据采集站与监控管理平台 — 开发基线需求规格说明书.md)
- [开发基线技术选型方案](docs/02视音频数据采集站与监控管理平台 — 开发基线技术选型方案.md)
- [开发环境搭建 — WSL2 与 KingbaseES V8R6 安装运维手册](docs/03开发环境搭建 — WSL2与KingbaseES V8R6 安装运维手册.md)
- [部署与运维手册](docs/04部署与运维手册.md)
- [开发规范](docs/05开发规范.md)
- [Git 与协作规范](docs/06Git与协作规范.md)
- [CI 与发布规范](docs/07CI与发布规范.md)
- [日常开发流程与检查清单](docs/08日常开发流程与检查清单.md)
- [桌面端 Windows 部署与运维手册](docs/04-1桌面端Windows部署与运维手册.md)
- [桌面端 Linux 麒麟统信部署与运维手册](docs/04-2桌面端Linux麒麟统信部署与运维手册.md)
- [平台 Windows 部署与运维手册](docs/04-3平台Windows部署与运维手册.md)
- [平台 Linux 麒麟统信部署与运维手册](docs/04-4平台Linux麒麟统信部署与运维手册.md)
