# 监控管理平台 Windows 部署与运维手册

> 适用对象：实施/运维人员。本册适用于 Windows Server 2019/2022 x64 裸机部署；
> 总纲见《部署与运维手册》（04）；Linux/麒麟/统信平台（Docker）见《平台 Linux 麒麟统信部署与运维手册》（04-4）。

## 一、你需要准备的东西

| 物品 | 说明 |
| :--- | :--- |
| Windows 服务器 | Windows Server 2019 / 2022 64 位 |
| 平台交付包 | 发布产物目录（含 `Station.Platform.Api.exe` 与 `wwwroot/`），找开发/交付人员获取 |
| 数据库 | 三选一：MySQL 8 / PostgreSQL 16 / 金仓 V8R6 的 **Windows 版安装包** |
| 管理员账号 | 安装软件、注册服务需要 |
| 安全密钥 | `PLATFORM_COMMAND_PRIVATE_KEY`（指令 SM2 私钥）、`PLATFORM_REPORTING_PUBLIC_KEY`（上报验签公钥） |
| 证书 | HTTPS 用（可选；客户提供正式证书） |

## 二、部署前检查（10 分钟）

右键"开始"→ **Windows PowerShell（管理员）**，逐条执行：

```powershell
(Get-CimInstance Win32_OperatingSystem) | Select-Object Caption, Version, OSArchitecture
$env:PROCESSOR_ARCHITECTURE
([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
Get-PSDrive C | Select-Object @{n='剩余GB';e={[math]::Round($_.Free/1GB,1)}}
[math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory/1GB,1)
Get-NetTCPConnection -LocalPort 5100 -ErrorAction SilentlyContinue
```

✅ 预期结果：Server 2019/2022、`AMD64`、管理员 `True`、磁盘 ≥ 40GB、内存 ≥ 8GB、5100 端口无输出（空闲）。

## 三、安装数据库

按现场选一种，**三选一**（平台会自动建表，只需把库和账号建好）。

### 3.1 安装 MySQL 8（Windows）

1. 运行 MySQL Installer，安装 **MySQL Server 8.0**，设置 root 密码；
2. 安装完成后打开 **MySQL Command Line Client**，输入 root 密码，执行：

   ```sql
   CREATE DATABASE station_platform CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
   CREATE USER 'station'@'%' IDENTIFIED BY '改成强密码!';
   GRANT ALL PRIVILEGES ON station_platform.* TO 'station'@'%';
   FLUSH PRIVILEGES;
   EXIT;
   ```

3. 验证：

   ```powershell
   mysql -h127.0.0.1 -ustation -p -e "SHOW DATABASES;"
   ```

   应能看到 `station_platform`。

### 3.2 安装 PostgreSQL 16（Windows）

1. 运行 EDB PostgreSQL 16 安装器，设 postgres 超级用户密码；
2. 打开 **pgAdmin** 或命令行 `psql`，执行：

   ```sql
   CREATE USER station WITH PASSWORD '改成强密码!';
   CREATE DATABASE station_platform OWNER station;
   ```

3. 验证：

   ```powershell
   psql -h127.0.0.1 -U station -d station_platform -c "SELECT 1;"
   ```

### 3.3 安装金仓 V8R6（Windows）

1. 运行 KingbaseES V8 安装向导（Windows 版），默认端口 54321，编码 UTF8；
2. 服务管理器中启动金仓服务；
3. 用 `ksql` 执行：

   ```sql
   CREATE DATABASE station_platform;
   CREATE USER station WITH PASSWORD '改成强密码!';
   GRANT ALL PRIVILEGES ON DATABASE station_platform TO station;
   ```

> 记下你选的库类型和连接信息，第五章配置要用。

## 四、放置平台程序

1. 在服务器创建目录，例如 `C:\platform`；
2. 把交付包**整个目录**（`Station.Platform.Api.exe`、`wwwroot/`、`appsettings.json` 等）拷贝进去；
3. 确认目录结构：

   ```powershell
   dir C:\platform
   ```

   应看到 `Station.Platform.Api.exe` 和 `wwwroot` 文件夹。

> 建议路径不要带空格（如不要 `C:\Program Files\平台`）。

### 4.1 内网 / 无网络现场提示（先读）

- 提前在有网机器备齐并拷入（U 盘/内网共享）：**平台交付包、数据库安装包（MySQL/PG/金仓）、
  .NET 8 Hosting Bundle（仅框架依赖发布需要）、HTTPS 证书**；
- 平台程序为自包含发布，安装与运行**不需要联网**；采集站与浏览器对接的是内网地址；
- 数据库装完即可使用，无需外网；安全密钥、证书、授权全部离线准备；
- 现场不要执行任何"在线更新"类操作；按本手册第三~九章顺序执行即可。

## 五、配置（连接数据库 + 安全密钥）

推荐用**环境变量**配置（优先级最高，也避免改坏文件）。管理员 PowerShell 执行：

```powershell
[Environment]::SetEnvironmentVariable("STATION__DB__PROVIDER", "MySql", "Machine")      # MySql / PostgreSQL / Kingbase
[Environment]::SetEnvironmentVariable("STATION__DB__CONNECTIONSTRING", "Server=127.0.0.1;Port=3306;Database=station_platform;Uid=station;Pwd=你的密码;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None", "Machine")
[Environment]::SetEnvironmentVariable("PLATFORM_COMMAND_PRIVATE_KEY", "平台指令私钥", "Machine")
[Environment]::SetEnvironmentVariable("PLATFORM_REPORTING_PUBLIC_KEY", "采集站上报公钥", "Machine")
[Environment]::SetEnvironmentVariable("ASPNETCORE_URLS", "http://+:5100", "Machine")
```

连接串按数据库类型替换（见下表）：

| 数据库 | 连接串 |
| :--- | :--- |
| MySQL | `Server=127.0.0.1;Port=3306;Database=station_platform;Uid=station;Pwd=密码;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None` |
| PostgreSQL | `Host=127.0.0.1;Port=5432;Database=station_platform;Username=station;Password=密码;` |
| 金仓 | `Host=127.0.0.1;Port=54321;Database=station_platform;Username=station;Password=密码;DbType=Kdbndp` |

> 也可以直接编辑 `C:\platform\appsettings.json` 的 `Station:Db` 与 `Platform` 节点，效果相同。

## 六、首次启动验证

### 6.1 前台启动测试

```powershell
cd C:\platform
.\Station.Platform.Api.exe --urls http://127.0.0.1:5100
```

保持窗口运行，另开一个 PowerShell 验证：

```powershell
curl.exe http://127.0.0.1:5100/api/v1/health
curl.exe http://127.0.0.1:5100/
```

✅ 预期结果：`health` 返回正常（如 `{"status":"ok"}` 之类），`/` 返回平台登录页。

### 6.2 修改默认管理员密码

首次启动自动创建默认管理员 `admin`（密码见交付说明，通常 `Admin@123`）：

1. 浏览器打开 `http://127.0.0.1:5100` 登录；
2. 立即进入"系统管理 → 用户"修改 `admin` 密码。

### 6.3 关闭前台窗口

验证通过后按 `Ctrl+C` 关掉前台窗口，进入第七章注册为服务。

## 七、注册 Windows 服务（开机自动运行）

### 方式 A：系统自带 sc.exe（推荐）

```powershell
sc.exe create StationPlatformApi binPath= "C:\platform\Station.Platform.Api.exe" start= auto
sc.exe start StationPlatformApi
sc.exe query StationPlatformApi
```

✅ 预期结果：`query` 显示 `STATE : RUNNING`。

日常管理命令：

```powershell
sc.exe stop StationPlatformApi      # 停止
sc.exe start StationPlatformApi     # 启动
sc.exe delete StationPlatformApi    # 删除服务（卸载时）
```

### 方式 B：NSSM（可选，日志更好管）

```powershell
nssm install StationPlatformApi "C:\platform\Station.Platform.Api.exe"
nssm set StationPlatformApi AppDirectory "C:\platform"
nssm set StationPlatformApi AppStdout "C:\platform\logs\out.log"
nssm set StationPlatformApi AppStderr "C:\platform\logs\err.log"
nssm start StationPlatformApi
```

> 服务账号需对 `C:\platform` 有读写权限（写日志、备份）。

## 八、防火墙放行

```powershell
netsh advfirewall firewall add rule name="Station Platform API 5100" dir=in action=allow protocol=TCP localport=5100
```

客户浏览器和采集站访问 `http://服务器IP:5100` 前必须先放行。

## 九、HTTPS（可选但生产建议）

### 方案 A：IIS 反向代理（推荐）

1. 服务器管理器安装 **IIS + ARR + URL Rewrite**；
2. 新建站点绑定 443 证书；
3. 配置反向代理转发到 `http://127.0.0.1:5100`。

### 方案 B：Kestrel 直接挂证书

```powershell
[Environment]::SetEnvironmentVariable("ASPNETCORE_URLS", "https://+:8443", "Machine")
[Environment]::SetEnvironmentVariable("ASPNETCORE_Kestrel__Certificates__Default__Path", "C:\certs\server.pfx", "Machine")
[Environment]::SetEnvironmentVariable("ASPNETCORE_Kestrel__Certificates__Default__Password", "证书密码", "Machine")
sc.exe stop StationPlatformApi; sc.exe start StationPlatformApi
```

## 十、日常运维

| 事项 | 操作 |
| :--- | :--- |
| 查看服务状态 | `sc.exe query StationPlatformApi` |
| 查看日志 | 程序目录 `logs/`（按天滚动）；事件查看器 → Windows 日志 → 应用程序 |
| 健康检查 | 浏览器/命令行访问 `http://127.0.0.1:5100/api/v1/health` |
| 数据库备份 | 平台每天 03:00 自动备份；再配计划任务定期 `mysqldump`/`pg_dump`（见总册第十章） |
| 升级 | 停止服务 → 备份数据库和 `appsettings.json` → 替换 `C:\platform` 内容（保留配置）→ 启动服务 |
| 回滚 | 停止服务 → 还原备份目录 → 启动服务 |

## 十一、常见问题

| 现象 | 处理 |
| :--- | :--- |
| 服务启动失败/超时（1053） | 先用第六章前台方式启动看报错；多半是连接串、端口占用或目录权限 |
| 首页白屏 | 确认 `wwwroot` 存在；访问 `/` 应返回登录页 |
| 金仓连不上 | 确认连接串含 `DbType=Kdbndp`、端口 54321、金仓服务已启动 |
| 登录后很快退出 | 是采集站端策略（`AutoLogoutMinutes`），与平台无关 |
| PDF 导出中文乱码 | Windows 自带中文字体，一般正常；异常时设 `STATION__EXPORT__FONTFILE` 指向字体文件 |
| 外网访问不通 | 检查防火墙 5100 放行、云安全组、路由器端口映射 |
| 无网络/内网环境 | 平台自包含、不需要联网；交付包与数据库安装包提前拷入，按 4.1 提示离线部署 |

## 十二、交付自检清单

- [ ] 系统检查通过（版本/架构/管理员/磁盘/端口）
- [ ] 数据库已安装并建好 `station_platform` 库与 `station` 账号
- [ ] 环境变量已配置（PROVIDER、连接串、两个安全密钥）
- [ ] 前台启动 health 与首页验证通过
- [ ] 默认管理员密码已改
- [ ] 服务注册成功且自启动（`start= auto`）
- [ ] 防火墙 5100 已放行
- [ ] HTTPS 可用（如需）
- [ ] 备份任务确认（每日自动 + 现场计划任务）
- [ ] 无网络现场：交付包/数据库安装包/证书已提前备齐并完成离线部署验证
