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
│   ├── Station.Domain/       公共核心实体
│   ├── Station.Application/  共享用例/服务抽象
│   ├── Station.Infrastructure/ 共享基础设施抽象
│   └── Directory.Build.props 共享层版本号（契约不兼容变更时递增）
├── Station.Desktop/          采集站桌面端（含单机版内置 Web）
│   └── Directory.Build.props 桌面端产品版本号（独立发版）
└── Station.Platform/         监控管理平台
    └── Directory.Build.props 平台端产品版本号（独立发版）
station-web/                  桌面端内置 Web 前端（Vue 3 + Vite，pnpm workspace 成员）
station-platform-web/         管理平台前端（Vue 3 + Vite，pnpm workspace 成员）
packages/
└── shared/                   @station/shared：两前端共用的请求封装 / 类型 / 工具
archive/                      历史代码与旧版文档（仅参考，不参与构建）
```

两个 Web 前端通过 **pnpm workspace** 管理（根目录 `package.json` + `pnpm-workspace.yaml`），
依赖在仓库根一次性安装；两前端共用的请求封装、公共类型与工具收敛在 `packages/shared`（`@station/shared`）。

## 构建

```powershell
# 前端依赖（pnpm workspace，仓库根执行一次；首次需安装 pnpm：npm install -g pnpm）
pnpm install

# 桌面端（含单机版内置 Web）
dotnet build .\Station.Desktop.sln -c Release

# 平台端
dotnet build .\Station.Platform.sln -c Release

# 前端（可选，单跑某一端）
pnpm --filter station-web build          # 桌面端内置 Web
pnpm --filter station-platform-web build # 平台前端
```

依赖方向：`Desktop → Shared`、`Platform → Shared`；`Shared` 不依赖任何一端，`Platform` 永不引用 `Desktop`。

## 版本与发版

桌面端与平台端是**独立交付的标准产品**，各自独立版本号、独立发版：

| 产品 | 版本号来源 | 发布标签 | release.yml 产物 |
| :--- | :--- | :--- | :--- |
| 桌面端 | `src/Station.Desktop/Directory.Build.props` | `desktop-vX.Y.Z` | Windows MSI + Linux DEB（x86_64 / ARM64） |
| 平台端 | `src/Station.Platform/Directory.Build.props` | `platform-vX.Y.Z` | Docker 镜像 tar.gz（`docker load` 导入） |

- 打哪个标签，`release.yml` 就只构建/发布哪个产品，不会因为另一端改动被迫跟着发版；
- 手动触发时在 Actions 页面选择产品并可覆盖版本号（留空读对应 `Directory.Build.props`）；
- 前端 `package.json` 的版本是内部依赖版本，**产品对外版本以两端 `Directory.Build.props` 为准**。

## 文档

- [开发基线需求规格说明书](docs/01视音频数据采集站与监控管理平台 — 开发基线需求规格说明书.md)
- [开发基线技术选型方案](docs/02视音频数据采集站与监控管理平台 — 开发基线技术选型方案.md)
- [开发环境搭建 — WSL2 与 KingbaseES V8R6 安装运维手册](docs/03开发环境搭建 — WSL2与KingbaseES V8R6 安装运维手册.md)
- [部署与运维手册](docs/04部署与运维手册.md)
- [开发规范](docs/05开发规范.md)
- [Git 与协作规范](docs/06Git与协作规范.md)
- [CI 与发布规范](docs/07CI与发布规范.md)
- [日常开发流程与检查清单](docs/08日常开发流程与检查清单.md)
- [开发环境搭建与本地调试指南（新成员入门）](docs/09开发环境搭建与本地调试指南.md)
- [桌面端 Windows 部署与运维手册](docs/04-1桌面端Windows部署与运维手册.md)
- [桌面端 Linux 麒麟统信部署与运维手册](docs/04-2桌面端Linux麒麟统信部署与运维手册.md)
- [平台 Windows 部署与运维手册](docs/04-3平台Windows部署与运维手册.md)
- [平台 Linux 麒麟统信部署与运维手册](docs/04-4平台Linux麒麟统信部署与运维手册.md)
