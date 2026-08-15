# 采集站桌面端 Linux / 麒麟 / 统信 部署与运维手册

> 适用对象：实施/运维人员。本册适用于麒麟（Kylin）、统信 UOS、Ubuntu/Debian
> 等 apt 系 Linux 桌面（x86_64 / arm64）；总纲见《部署与运维手册》（04），Windows 桌面端见《桌面端 Windows 部署与运维手册》（04-1）。

## 一、你需要准备的东西

| 物品 | 说明 |
| :--- | :--- |
| Linux 电脑 | 麒麟 / 统信 UOS / Ubuntu / Debian，带**图形界面**；x86_64 或 arm64 |
| 安装包 | `station-desktop_<版本>_amd64.deb`（x86_64）或 `station-desktop_<版本>_arm64.deb`（arm64） |
| sudo 权限 | 安装软件需要（安装时输入当前用户密码即可） |
| 现场信息 | 采集源模式、存储方式、平台地址与站编号（平台版才需要） |
| 授权文件 | 正式授权由内部工具生成（可选，试用期可不装） |

> 说明：桌面端默认使用内置 SQLite 数据库，**不需要安装数据库**；程序为自包含发布，**不需要安装 .NET**。

## 二、部署前检查（10 分钟）

打开"终端"（应用列表里搜"终端/Terminal"），逐条复制执行：

### 2.1 检查系统与架构

```bash
cat /etc/os-release | head -3
uname -m
```

✅ 预期结果：看到系统名称（如 Kylin V10 / UOS 20 / Ubuntu），架构为 `x86_64` 或 `aarch64`。
架构决定用哪个安装包：`x86_64` → `_amd64.deb`，`aarch64` → `_arm64.deb`。

### 2.2 检查权限与磁盘

```bash
id -u
df -h /
free -h
```

✅ 预期结果：`id -u` 输出 0 表示 root（否则安装命令前要加 `sudo`）；剩余磁盘 ≥ 20GB、内存 ≥ 4GB。

### 2.3 检查桌面图形依赖

```bash
ldconfig -p | grep -E 'libX11|libxcb|libxkbcommon|libfontconfig' | head -20 || echo "缺少图形依赖"
```

✅ 预期结果：能列出若干库文件；如果提示"缺少图形依赖"，先按第四章安装依赖再装软件。

### 2.4 检查端口

```bash
ss -tlnp | grep -E ':5000|:5100' || echo "端口空闲"
```

✅ 预期结果：输出"端口空闲"；有输出说明被占用，需先处理。

## 三、安装（推荐：DEB 安装包）

### 3.1 安装

把安装包放到本机（例如下载目录），打开终端进入该目录后执行：

```bash
cd ~/下载          # 换成安装包实际所在目录
sudo apt-get update
sudo apt-get install -y ./station-desktop_0.1.0_amd64.deb
```

> arm64 机器把文件名换成 `station-desktop_0.1.0_arm64.deb`。

✅ 验证安装：

```bash
ls -l /usr/lib/station-desktop/Station.Desktop.UI
```

预期能看到文件；同时应用菜单里出现 **Station Desktop** 图标。

### 3.2 内网 / 无网络安装（离线）

目标机没有外网时，**不要执行 `sudo apt-get update`**（会卡在连接超时），直接本地安装：

```bash
cd ~/下载          # 换成安装包实际所在目录
sudo dpkg -i ./station-desktop_0.1.0_amd64.deb
```

- 麒麟/统信及多数发行版自带所需图形依赖，一般可直接装成功；
- 若提示缺少依赖：先按第四章"离线补装依赖"处理再重试；
- 安装包与依赖包提前在有网机器下载，通过 U 盘/内网共享拷入；
- 平台版对接内网平台地址即可，全程无需外网。

### 3.3 升级

```bash
sudo apt-get install -y ./station-desktop_0.2.0_amd64.deb
```

配置（`appsettings.json`）是 conffile，升级自动保留。

### 3.4 卸载

```bash
sudo apt-get remove station-desktop
```

## 四、依赖与中文字体（缺失时安装）

```bash
sudo apt-get update
sudo apt-get install -y libx11-6 libxcb1 libxkbcommon0 libfontconfig1 libxrandr2 libxcursor1 libxi6 libice6 libsm6 libgl1
sudo apt-get install -y fonts-wqy-microhei
```

✅ 安装后重新执行 2.3 检查，应能列出库文件。

**离线补装依赖（无外网时）**：在有网机器上先下载依赖包，拷入目标机后本地安装：

```bash
# 有网机器：下载依赖 deb
apt-get download libx11-6 libxcb1 libxkbcommon0 libfontconfig1 libxrandr2 libxcursor1 libxi6 libice6 libsm6 libgl1 fonts-wqy-microhei

# 目标机：本地安装（不要 apt-get update）
sudo dpkg -i *.deb
```

> 麒麟/UOS 一般自带上述依赖，可先直接 `dpkg -i` 主安装包，缺哪个再离线补哪个。

## 五、拷贝部署（没有安装包时）

1. 拿到发布目录 `publish/desktop/linux-x64/`（或 `linux-arm64/`）；
2. 拷贝到目标机并加执行权限：

   ```bash
   sudo mkdir -p /opt/station-desktop
   sudo cp -r linux-x64/* /opt/station-desktop/
   sudo chmod +x /opt/station-desktop/Station.Desktop.UI
   ```

3. 启动：`/opt/station-desktop/Station.Desktop.UI`（需图形界面）。

## 六、配置（appsettings.json）

程序默认按内置配置运行，通常**只需在平台版或改存储时才需要编辑**。

编辑 `/usr/lib/station-desktop/appsettings.json`（拷贝部署则为对应目录）：

```bash
sudo nano /usr/lib/station-desktop/appsettings.json
```

按需修改后保存（`Ctrl+O` 回车，`Ctrl+X` 退出）：

```json
{
  "Station": {
    "Collect": { "SourceMode": "ums" },
    "Storage": { "Target": "Local", "LocalRoot": "", "StationNo": "ST0001" },
    "Platform": { "Enabled": false, "BaseUrl": "http://192.168.1.10:5100", "StationCode": "ST0001" },
    "Command": { "PublicKeyPem": "", "Required": false }
  }
}
```

| 配置项 | 填什么 |
| :--- | :--- |
| `Collect:SourceMode` | `ums` 接真机采集；`simulated` 开发测试用 |
| `Storage:Target` | `Local` 存本机；`Ftp` / `Sftp` 传远端 |
| `Platform:Enabled` | `false` 单机版；`true` 平台版（再填 `BaseUrl` 和 `StationCode`） |
| `Command:PublicKeyPem` | 平台下发的验签公钥（由交付人员提供） |

## 七、首次启动与验证

1. 应用菜单点击 **Station Desktop**（或终端执行 `/usr/bin/station-desktop`）；
2. 确认界面里的**授权状态**；
3. 单机版：浏览器打开 `http://127.0.0.1:5000` 能看到内置 Web 界面；
4. 平台版：登录平台后台，台账里能看到该站"在线"；
5. 有条件时插记录仪 U 盘做一次真实采集冒烟。

## 八、开机自启（可选）

```bash
mkdir -p ~/.config/autostart
cp /usr/share/applications/station-desktop.desktop ~/.config/autostart/
```

取消自启：删除 `~/.config/autostart/station-desktop.desktop`。

## 九、日常运维

### 9.1 数据与日志位置

| 内容 | 位置 |
| :--- | :--- |
| 本地数据库 | `~/.local/share/Station/station.db`（默认，可用 `STATION__DATA__DIR` 覆盖） | 
| 运行时设置 | `~/.local/share/Station/appsettings.runtime.json`（系统设置页修改后落盘，重启生效） | 
| 采集文件 | `Station:Storage:LocalRoot` 指定目录 |
| 日志 | 程序目录 `logs/`（按天滚动） |

### 9.2 备份（建议每周）

```bash
# 先关闭桌面端程序，再备份
cp ~/.local/share/Station/station.db /备份目录/station-$(date +%F).db
```

采集文件目录按同样方式整体复制。

### 9.3 升级

1. 备份（见 9.2）；
2. `sudo apt-get install -y ./station-desktop_新版本_amd64.deb`；
3. 启动验证（见第七章）。

## 十、常见问题

| 现象 | 处理 |
| :--- | :--- |
| 点图标没反应 | 终端执行 `/usr/bin/station-desktop` 看报错；多数是缺图形依赖，按第四章安装 |
| 提示缺 libxxx | 执行第四章的依赖安装命令 |
| 中文显示方块 | 安装中文字体：`sudo apt-get install -y fonts-wqy-microhei` |
| 单机版网页打不开 | 确认程序已启动；本机访问 `http://127.0.0.1:5000` |
| 指纹 unknown | 属正常降级（/sys/class/dmi 读取权限）；确认以普通用户运行 |
| 接 U 盘不识别 | 确认 `SourceMode=ums`；检查设备接入；桌面环境需允许自动挂载 |
| 平台不上线 | 确认 `Platform:Enabled=true`、`BaseUrl` 可达、`StationCode` 一致 |
| 没外网装不上/`apt-get update` 超时 | 按 3.2 用 `dpkg -i` 本地安装，不要执行 `apt-get update`；缺依赖按第四章离线补 |

## 十一、交付自检清单

- [ ] 系统检查通过（架构选对安装包、权限、磁盘）
- [ ] 图形依赖与中文字体已装
- [ ] 安装完成，菜单出现 Station Desktop
- [ ] 程序启动正常，授权状态确认
- [ ] 单机版 Web（5000）或平台版上线验证通过
- [ ] 真机采集冒烟通过（如现场有设备）
- [ ] 开机自启已配置（如需）
- [ ] 备份策略已告知客户
- [ ] 无网络现场：已用本地 deb 离线安装并验证（不依赖外网）
