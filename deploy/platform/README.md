# 监控管理平台 - Docker Compose 部署

交付包：`deploy/platform/`（Dockerfile + 三个 Compose 文件 + `.env.example`）。镜像内含后端 API 与前端静态资源（Vue 构建产物已在 wwwroot 中）。

## 前置

- Docker 20.10+ / Docker Compose v2；
- 镜像构建上下文 = **仓库根目录**（Compose 已用 `context: ../..` 指向）。

## 一、MySQL（默认）

```bash
cp deploy/platform/.env.example deploy/platform/.env   # 按需修改密码/端口
docker compose -f deploy/platform/docker-compose.yml --env-file deploy/platform/.env up -d
```

- 访问 `http://<主机>:5100`，默认账号 `admin / Admin@123`（首次启动自动建库建表、播种权限/角色/管理员）；
- 数据持久化：命名卷 `mysql-data`；
- 自动备份：平台库每日 03:00 自动逻辑备份（SQL，可还原）到命名卷 `backups`，保留份数 `BACKUP_RETENTION`（默认 30）；界面"系统管理"可手动备份/查看备份列表；
- 备份：`docker compose -f deploy/platform/docker-compose.yml exec mysql sh -c 'mysqldump -ustation -p$MYSQL_PASSWORD station_platform' > backup.sql`。

## 二、PostgreSQL

```bash
docker compose -f deploy/platform/docker-compose.pg.yml --env-file deploy/platform/.env up -d
```

## 三、Kingbase（金仓）

金仓建议部署在国产主机（不容器化）。平台 API 容器连接**外部金仓**：

1. `.env` 配置 `KINGBASE_CONNECTION=Host=...;Port=54321;Database=...;Username=...;Password=...`；
2. 容器能访问金仓主机：Docker Desktop 用 `host.docker.internal`，Linux 容器用宿主机 IP 或 `--add-host`；
3. 启动：

```bash
docker compose -f deploy/platform/docker-compose.kingbase.yml --env-file deploy/platform/.env up -d
```

三库切换只改 `STATION__DB__PROVIDER`（MySql/PostgreSQL/Kingbase）与连接串，业务代码与建表自动适配。

## 四、HTTPS

- **方案 A（推荐）**：前置 Nginx/Caddy 反向代理终结 TLS，转发到 `http://容器:8080`；证书用客户提供的正式证书（国密场景由网关处理）；
- **方案 B**：Kestrel 直接挂证书，在 Compose 的 api 服务追加：

```yaml
environment:
  ASPNETCORE_URLS: https://+:8443
  ASPNETCORE_Kestrel__Certificates__Default__Path: /certs/server.pfx
  ASPNETCORE_Kestrel__Certificates__Default__Password: "${CERT_PASSWORD}"
volumes:
  - ./certs:/certs:ro
```

## 五、生产必配：安全密钥

- `PLATFORM_COMMAND_PRIVATE_KEY`：平台指令 SM2 私钥（指令签名，采集站内置对应公钥）；不配则指令以开发模式（`unsigned`）下发；
- `PLATFORM_REPORTING_PUBLIC_KEY`：采集站上报验签公钥；不配则上报跳过验签；
- 密钥由内部工具生成（SM2 密钥对），私钥给平台、公钥内置到采集站。

## 六、日志 / 升级 / 恢复

- 日志：`docker compose -f deploy/platform/docker-compose.yml logs -f api`；
- 升级：停服 → 备份数据库 → `docker compose ... build` → `up -d`；
- 恢复：先建库再导入备份（`mysql < backup.sql` / `psql < backup.sql`），表结构与种子由平台启动时自动初始化。
