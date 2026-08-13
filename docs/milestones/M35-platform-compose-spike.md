# M35：平台 Docker Compose 交付包

> 状态：✅ 通过（2026-08-13，ComposeValidate 5/5 + 发布产物冒烟通过）
> 目标：交付平台端"一条命令起服务"的容器化部署包，MySQL/PostgreSQL/Kingbase 三库可切换，镜像内含前端。

## 1. 交付内容

- [deploy/platform/Dockerfile](../../deploy/platform/Dockerfile)：多阶段（sdk 8.0 构建 → aspnet 8.0 运行），构建上下文为仓库根目录，产物含后端 API + wwwroot 前端静态资源；
- `docker-compose.yml`（MySQL 8，含健康检查与数据卷）、`docker-compose.pg.yml`（PostgreSQL 16）、`docker-compose.kingbase.yml`（API 连接外部金仓）；
- `.env.example`：端口、默认管理员、MySQL/PG 密码、金仓连接、指令私钥/上报公钥；
- [README.md](../../deploy/platform/README.md)：三库启动、HTTPS 两种方案（Nginx 反代推荐 / Kestrel 证书）、安全密钥、日志/升级/恢复；
- [smoke-published-api.ps1](../../scripts/smoke-published-api.ps1)：发布产物冒烟（Vue 首页 + 登录 + 文件接口）。

## 2. 修复

- **wwwroot 未随发布输出**：平台 API 使用普通 `Microsoft.NET.Sdk`，前端产物不在发布包里（Docker 镜像会缺前端）→ csproj 增加 `Content Include="wwwroot\**" CopyToPublishDirectory`，发布产物现含 wwwroot 23 个文件。

## 3. 验证结果

| 场景 | 结果 |
|---|---|
| Compose 结构：mysql/pg/kingbase 三个文件服务完整 | ✅ |
| 三库切换环境变量（MySql/PostgreSQL/Kingbase） | ✅ |
| 健康检查与数据卷存在 | ✅ |
| .env 模板关键项齐全 | ✅ |
| Dockerfile sdk→publish→aspnet 结构完整 | ✅ |
| dotnet publish 产物含 wwwroot（index.html + 22 assets） | ✅ |
| 发布产物冒烟：Vue 首页 / admin 登录 / 文件接口 200 | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.ComposeValidate -c Release
powershell -File scripts/smoke-published-api.ps1
```

## 4. 说明与后续（M36）

- 本机无 Docker，镜像构建与容器启动需在目标环境执行（`docker compose -f deploy/platform/docker-compose.yml up -d`）；
- M36：部署与运维手册（环境要求、授权激活流程、升级、备份恢复、信创部署清单）。
