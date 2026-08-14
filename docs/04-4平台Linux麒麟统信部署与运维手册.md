# 监控管理平台 Linux / 麒麟 / 统信 部署与运维手册

> 适用对象：实施/运维人员。本册适用于 Ubuntu/Debian、麒麟、统信 UOS 的
> **Docker Compose 部署**；总纲见《部署与运维手册》（04），Windows 裸机平台见《平台 Windows 部署与运维手册》（04-3）。

## 一、你需要准备的东西

| 物品 | 说明 |
| :--- | :--- |
| Linux 服务器 | x86_64 或 arm64；Ubuntu/Debian/麒麟/统信 |
| 交付包 | `deploy/platform/` 目录（Dockerfile + 三个 Compose 文件 + `.env.example`） |
| Docker | Docker 20.10+ 与 Compose v2（没有的话按第三章安装） |
| 管理员权限 | 安装 Docker、执行命令需要 `sudo` |
| 安全密钥 | `PLATFORM_COMMAND_PRIVATE_KEY`（指令 SM2 私钥）、`PLATFORM_REPORTING_PUBLIC_KEY`（上报验签公钥） |
| 数据库 | 默认由 Compose 自动拉起 MySQL/PostgreSQL；金仓走外部数据库 |

> 平台程序在 Docker 容器内运行，服务器**不需要安装 .NET**；数据库用 MySQL/PostgreSQL 时也**不需要手工装库**。

## 二、部署前检查（10 分钟）

打开"终端"，逐条执行：

```bash
cat /etc/os-release | head -3
uname -m
id -u
df -h /
free -h
docker --version && docker compose version
ss -tlnp | grep -E ':5100' || echo "端口空闲"
```

✅ 预期结果：
- 看到系统名称与架构（`x86_64` / `aarch64` 均可）；
- `id -u` 输出 0 是 root（否则命令前加 `sudo`）；
- 磁盘 ≥ 40GB、内存 ≥ 8GB；
- `docker --version` 和 `docker compose version` 都有输出版本号（没有就执行第三章）；
- 5100 端口空闲。

## 三、安装 Docker 与 Compose

### 3.1 在线安装（Ubuntu/Debian）

```bash
sudo apt-get update
sudo apt-get install -y ca-certificates curl
curl -fsSL https://get.docker.com | sudo sh
sudo systemctl enable --now docker
sudo usermod -aG docker $USER
newgrp docker
sudo apt-get install -y docker-compose-plugin
docker compose version
```

### 3.2 麒麟/统信（无外网时离线安装）

1. 在有网的**同架构**机器下载四个包：`docker-ce`、`containerd.io`、`docker-ce-cli`、`docker-compose-plugin`；
2. 拷到目标机后执行：

   ```bash
   sudo dpkg -i docker-ce_*.deb containerd.io_*.deb docker-ce-cli_*.deb docker-compose-plugin_*.deb
   sudo systemctl enable --now docker
   docker compose version
   ```

### 3.3 国内镜像加速（拉镜像慢时）

```bash
sudo mkdir -p /etc/docker
sudo tee /etc/docker/daemon.json <<'EOF'
{ "registry-mirrors": ["https://docker.m.daocloud.io", "https://mirror.ccs.tencentyun.com"] }
EOF
sudo systemctl restart docker
```

## 四、放置交付包并配置 .env

1. 把 `deploy/platform/` 整个目录拷贝到服务器，例如 `/opt/station-platform`：

   ```bash
   sudo mkdir -p /opt/station-platform
   sudo cp -r deploy/platform/* /opt/station-platform/
   cd /opt/station-platform
   ```

2. 生成环境配置：

   ```bash
   cp .env.example .env
   nano .env
   ```

3. 至少修改以下几项（用 `Ctrl+O` 保存，`Ctrl+X` 退出）：

   | 变量 | 填什么 |
   | :--- | :--- |
   | `MYSQL_ROOT_PASSWORD` / `MYSQL_PASSWORD` | 数据库密码，**生产必须改**（PostgreSQL 方式改 `PG_PASSWORD`） |
   | `ADMIN_PASSWORD` | 平台管理员密码，**生产必须改**（默认 `Admin@123`） |
   | `PLATFORM_COMMAND_PRIVATE_KEY` | 平台指令 SM2 私钥（找交付人员） |
   | `PLATFORM_REPORTING_PUBLIC_KEY` | 采集站上报验签公钥（找交付人员） |
   | `PLATFORM_HTTP_PORT` | 对外端口，默认 5100，一般不用改 |
   | `KINGBASE_CONNECTION` | 用金仓时填外部金仓连接串 |

## 五、启动平台

按现场数据库选一种，**三选一**：

```bash
cd /opt/station-platform

# MySQL（默认，推荐）
docker compose -f docker-compose.yml --env-file .env up -d

# PostgreSQL
docker compose -f docker-compose.pg.yml --env-file .env up -d

# 金仓（外部数据库）
docker compose -f docker-compose.kingbase.yml --env-file .env up -d
```

查看状态与日志：

```bash
docker compose -f docker-compose.yml --env-file .env ps
docker compose -f docker-compose.yml --env-file .env logs -f api
```

✅ 首次启动约 1~2 分钟（要等数据库就绪），看到 `api` 状态为 `running` 即成功。

## 六、验证

```bash
curl http://127.0.0.1:5100/api/v1/health
```

✅ 预期结果：返回正常状态（如 `{"status":"ok"}`）。

浏览器访问 `http://服务器IP:5100`：

1. 用默认管理员登录（账号 `admin`，密码见 .env 的 `ADMIN_PASSWORD`）；
2. **立即修改默认密码**：系统管理 → 用户 → 改密；
3. 验证平台页面正常（台账/采集站列表能打开）。

## 七、HTTPS（可选但生产建议）

### 方案 A：Nginx 反向代理（推荐）

在服务器上装 Nginx，配置：

```nginx
server {
    listen 443 ssl;
    server_name platform.example.com;
    ssl_certificate     /etc/nginx/certs/server.crt;
    ssl_certificate_key /etc/nginx/certs/server.key;
    location / {
        proxy_pass http://127.0.0.1:5100;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

```bash
sudo nginx -t && sudo systemctl reload nginx
```

### 方案 B：Kestrel 直接挂证书

在 `.env` 增加证书变量并在 compose 的 api 服务挂证书卷（详见 deploy/platform/README.md）。

## 八、日常运维

| 事项 | 命令/说明 |
| :--- | :--- |
| 查看状态 | `docker compose -f docker-compose.yml --env-file .env ps` |
| 查看日志 | `docker compose -f docker-compose.yml --env-file .env logs -f api` |
| 健康检查 | `curl http://127.0.0.1:5100/api/v1/health` |
| 停止（保留数据） | `docker compose -f docker-compose.yml --env-file .env down` |
| 完全清理（含数据卷，慎用） | `docker compose -f docker-compose.yml --env-file .env down -v` |
| 备份 | 平台每天 03:00 自动备份到 `backups` 卷（保留 30 份）；另有数据库卷，见总册第十章 |
| 升级 | 停服 → 备份数据库 → `docker compose -f docker-compose.yml --env-file .env build` → `up -d` |
| 回滚 | 用上一版镜像（`docker tag` 指回旧镜像）或还原数据库备份后启动 |

## 九、离线交付（无外网现场）

内网/无外网现场三步走：
① 在有网机器备好 **Docker 安装包（同架构）+ 离线镜像 tar + 交付包 deploy/platform/**；
② 通过 U 盘/内网共享拷入目标机；
③ 按 3.2 安装 Docker → 按下方导入镜像 → 按第五章启动，全程无需外网。

在有网机器上导出镜像，目标机导入后按第五章启动：

```bash
# 构建机
cd deploy/platform
docker compose -f docker-compose.yml build
docker save station-platform-api:0.1.0 | gzip > station-platform-api-0.1.0.tar.gz
docker save mysql:8.0 | gzip > mysql-8.0.tar.gz          # 按实际库镜像导出

# 目标机
docker load < station-platform-api-0.1.0.tar.gz
docker load < mysql-8.0.tar.gz
```

> 金仓模式不需要库镜像，直接连外部金仓。

## 十、常见问题

| 现象 | 处理 |
| :--- | :--- |
| `docker: command not found` | 先按第三章安装 Docker |
| 拉镜像很慢/失败 | 按 3.3 配国内镜像加速；或走第九章离线交付 |
| `api` 一直重启 | `docker compose ... logs -f api` 看日志；多为数据库密码/连接串不一致或 5100 被占用 |
| 金仓连不上 | 核对 `.env` 的 `KINGBASE_CONNECTION`：主机可达、端口 54321、账号密码正确 |
| 首页白屏 | 访问 `/` 应返回登录页；确认 `wwwroot` 在镜像内（0.1.0 起已内置） |
| 默认管理员登录失败 | 首次启动才播种；确认 `.env` 的 `ADMIN_USERNAME`/`ADMIN_PASSWORD` 与交付说明一致 |
| 采集站不上线 | 确认采集站 `BaseUrl` 指向平台 5100（或 HTTPS 端口）且网络可达；平台侧台账看在线状态 |
| 内网拉不到镜像 | 不走在线拉取；按第九章离线导入镜像后再启动 |

## 十一、交付自检清单

- [ ] 系统检查通过（架构/Docker/磁盘/端口）
- [ ] Docker 与 Compose 已装好（或离线导入镜像）
- [ ] `.env` 已配置（密码、两个安全密钥）
- [ ] 平台启动成功，`api` 为 running
- [ ] health 与登录页验证通过
- [ ] 默认管理员密码已改
- [ ] HTTPS 可用（如需）
- [ ] 金仓现场：外部金仓连通验证通过
- [ ] 离线现场：Docker 安装包与离线镜像已导入/安装
- [ ] 备份策略确认（每日自动 + 现场备份）
