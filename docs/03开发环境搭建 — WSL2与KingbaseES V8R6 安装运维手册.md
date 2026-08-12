# 开发环境搭建 — WSL2 与 KingbaseES V8R6 安装运维手册

> 适用对象：开发/测试环境（本机 Windows 11 + WSL2 内运行金仓数据库）
> 验证日期：2026-08-12（本机全流程实测通过，数据库版本 V008R006C009B0014）
> 关联文档：[02 开发基线技术选型方案](02视音频数据采集站与监控管理平台 — 开发基线技术选型方案.md)、[M2 Kingbase Spike 记录](milestones/M2-kingbase-spike.md)

## 1. 文档目的

记录在本机从零搭建 **WSL2 → Ubuntu 22.04 → KingbaseES V8R6（Linux x64）** 的完整过程，包括：WSL2 安装、Ubuntu 安装与用户配置、金仓 ISO 下载校验、数据库安装（控制台向导）、日常启动/停止/连接，以及常见故障与注意事项。后续新同事或新机器可照此手册复现。

## 2. 环境清单

| 项 | 版本/值 |
|---|---|
| 宿主机 | Windows 11 企业版（Build 22631），x86_64 |
| WSL | WSL2（内核 5.10.16.3-microsoft-standard-WSL2） |
| 发行版 | Ubuntu 22.04.1 LTS |
| Linux 用户 | `kingbase`（默认用户，sudo 组） |
| 数据库 | KingbaseES V8R6C9B14（V008R006C009B0014），ORACLE 兼容模式 |
| 安装目录 | `/opt/Kingbase/ES/V8`，数据目录 `/opt/Kingbase/ES/V8/data` |
| 端口/账号 | `54321` / `system`（密码见本机记录，勿外传） |
| 字符集 | UTF8，locale 使用 C（见 10.4 说明） |
| 授权 | 官网"开发版"授权文件（安装 SERVER 必需） |
| .NET 驱动 | Kdbndp 8.0.0 + SqlSugarCore 5.1.4.216（已验证） |

## 3. 总体流程

```text
检查前置条件 → 启用 WSL2 功能并重启 → 安装 WSL2 内核 → 安装 Ubuntu 22.04
→ 创建 kingbase 用户并设为默认 → 下载并校验金仓 ISO → 挂载 ISO
→ 控制台安装金仓 → 启动服务并验证 → 日常运维
```

## 4. 步骤一：检查前置条件

PowerShell 中执行：

```powershell
wsl --status          # 查看 WSL 状态
wsl -l -v             # 查看已安装发行版
systeminfo | Select-String "Hyper-V"   # 确认已存在 Hypervisor（虚拟化已启用）
```

- 必须为 **x86_64** 的 Windows 10 2004+/Windows 11。
- 若 `systeminfo` 显示 "A hypervisor has been detected"，说明虚拟化可用，WSL2 可装。
- 启用 Windows 功能需要**管理员权限**（安装过程中会弹 UAC，需点"是"）。

## 5. 步骤二：安装 WSL2

### 5.1 启用两个 Windows 功能（管理员 PowerShell）

将以下内容保存为 `wsl2-setup.ps1`，右键"使用 PowerShell 运行"（或提权执行）：

```powershell
dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart
dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart
wsl.exe --set-default-version 2
```

> 关键点：启用 `VirtualMachinePlatform` 后，系统提示 **"Reboot required=yes"**，**必须重启电脑**，否则 WSL 相关命令会卡住（实测 `wsl --install` 卡在 WIM 挂载环节反复报错 `0xc142011c`）。

### 5.2 重启后安装 WSL2 内核

`wsl --update --web-download` 在国内网络会从 GitHub 下载，实测长时间无进展。**推荐直接下载微软 Azure Blob 上的内核 MSI**（约 17MB，速度快）：

```powershell
curl.exe -L -o E:\tools\wsl_update_x64.msi `
  https://wslstorestorage.blob.core.windows.net/wslblob/wsl_update_x64.msi

# 管理员安装（静默）
msiexec /i E:\tools\wsl_update_x64.msi /qn /norestart

# 设置默认版本为 WSL2
wsl --set-default-version 2
```

验证：

```powershell
wsl --status   # 显示默认版本 2、内核 5.10.16
```

## 6. 步骤三：安装 Ubuntu 22.04

### 6.1 方式 A：微软商店（推荐，最快）

管理员 PowerShell：

```powershell
wsl --install -d Ubuntu -n    # -n 表示安装后不启动，避免交互式建用户
```

### 6.2 方式 B：商店不可用时的绕行（本机实测路径）

企业版/商店受限时 `wsl --install -d Ubuntu` 会长时间无输出。改用官方直链包：

```powershell
# 1) 下载 Ubuntu 22.04 AppxBundle（约 1.07GB）
curl.exe -L -o E:\tools\Ubuntu2204.AppxBundle https://aka.ms/wslubuntu2204

# 2) 注册应用包（无需管理员）
Add-AppxPackage -Path E:\tools\Ubuntu2204.AppxBundle

# 3) 用 Ubuntu 启动器免交互注册 WSL 实例（--root 跳过建用户）
& "$env:LOCALAPPDATA\..\Program Files\WindowsApps\CanonicalGroupLimited.Ubuntu_2204.1.7.0_x64__79rhkp1fndgsc\ubuntu.exe" install --root
```

> 说明：启动器路径中的版本号（`2204.1.7.0`）随商店版本变化，用 `Get-AppxPackage -Name "*Ubuntu*"` 查看实际 `InstallLocation`。

验证：

```powershell
wsl -l -v   # 应显示 Ubuntu  Running / 2
```

## 7. 步骤四：创建 kingbase 用户并设为默认

```powershell
# 以 root 进入 Ubuntu 执行用户创建与配置
wsl -u root -e bash -lc "
id kingbase 2>/dev/null || useradd -m -s /bin/bash kingbase;
echo 'kingbase:Kingbase@123' | chpasswd;
usermod -aG sudo kingbase;
printf '[user]\ndefault=kingbase\n' > /etc/wsl.conf;
"

# 重启 WSL 使默认用户生效
wsl --shutdown
Start-Sleep -Seconds 5
wsl whoami   # 应输出 kingbase
```

> 密码说明：Linux 用户 `kingbase` 的密码是 `Kingbase@123`；金仓数据库超级用户 `system` 的密码在安装时自定义（本机为 `Test@123`），两者不同，勿混淆。
> 忘记 kingbase 密码时用 `wsl -u root -e bash -lc "echo 'kingbase:Kingbase@123' | chpasswd"` 重置。

## 8. 步骤五：下载并校验金仓 ISO

官方 OSS 直链（无需登录，阿里云北京节点，本机约 70MB/s）：

```powershell
curl.exe -L -o E:\Reny\station\archive\KingbaseES\KingbaseES_V008R006C009B0014_Lin64_install.iso `
  "https://kingbase.oss-cn-beijing.aliyuncs.com/KESV8R3/V008R006C009B0014/KingbaseES_V008R006C009B0014_Lin64_install.iso"

certutil -hashfile E:\Reny\station\archive\KingbaseES\KingbaseES_V008R006C009B0014_Lin64_install.iso MD5
```

官方 MD5：`742F40A9B2B8A61DB1F9AC8AD46F3F18`（必须一致再继续）。

## 9. 步骤六：挂载 ISO 与安装前准备

### 9.1 关键认知：WSL2 空闲自动关机

WSL2 在**无活动会话约 8 秒后自动关闭虚拟机**，loop 挂载会随之丢失。因此"挂载 → 安装"必须在**同一个 WSL 会话**内完成，或全程保持一个 `wsl` 终端窗口不关闭。

### 9.2 安装依赖并挂载

进入 WSL（默认用户 kingbase），执行：

```bash
sudo apt-get update -y
sudo apt-get install -y libaio1 libnuma1 rsync bc

sudo mkdir -p /mnt/kbiso
sudo mount -o loop /mnt/e/Reny/station/archive/KingbaseES/KingbaseES_V008R006C009B0014_Lin64_install.iso /mnt/kbiso
ls /mnt/kbiso        # 应看到 setup.sh 与 setup/ 目录
```

## 10. 步骤七：安装 KingbaseES（控制台向导，推荐）

### 10.1 准备授权文件（必需）

安装集含 SERVER 组件时**必须提供 license 文件**，否则数据库服务无法启动（安装过程也可能异常）。获取方式：

1. 浏览器登录金仓官网下载中心：`https://www.kingbase.com.cn/download.html`
2. 路径：数据库 → KingbaseES V8 R6 → **授权文件（开发版）**
3. 下载 `license_V8R6-开发版.zip` 并解压，得到 `license_xxxxx.dat`
4. 放入本机，例如 `E:\Reny\station\archive\KingbaseES\license.dat`（WSL 内路径 `/mnt/e/Reny/station/archive/KingbaseES/license.dat`）

### 10.2 授权安装目录写权限

```bash
sudo mkdir -p /opt/Kingbase
sudo chown -R kingbase:kingbase /opt/Kingbase
```

> 未授权时安装器报错：`You do not have write permissions to the chosen installation destination.`

### 10.3 启动控制台安装

```bash
cd /mnt/kbiso
sh setup.sh -i console
```

按提示操作对照表：

| 提示 | 输入 | 说明 |
|---|---|---|
| 选择实例 | `1` | 安装新的实例 |
| 简介 | 回车 | 直接下一步 |
| 许可协议 | 连续回车翻页，最后 `Y` | 接受协议 |
| 安装集 | `1` | 完全安装 |
| 授权文件路径 | `/mnt/e/.../license.dat` | 填第 10.1 步的绝对路径 |
| 安装文件夹 | `/opt/Kingbase/ES/V8` | 回车确认后输入 `Y` |
| 数据目录 | 回车 | 默认安装目录下 data |
| 端口 | `54321` | 与产品默认一致 |
| 用户名 | `system` | 超级用户 |
| 密码/确认 | `Test@123` | 自定义，需牢记 |
| 字符集 | `UTF8` | 与产品要求一致 |
| 兼容模式 | `ORACLE` | 产品按 ORACLE 模式适配 |
| 大小写敏感 | `YES` | ORACLE 模式默认 |
| 数据块大小 | `8k` | 默认 |
| Locale | `1`（C） | 见 10.4 说明 |

等待进度 100%（约 3~5 分钟），最后回车退出。

### 10.4 常见报错与处理

| 报错 | 原因 | 处理 |
|---|---|---|
| `Locale not supported by the OS` | Ubuntu 未生成 zh_CN/en_US locale | 直接选 `1`（C）；或先 `sudo locale-gen zh_CN.UTF-8 en_US.UTF-8` 再重装 |
| `You do not have write permissions...` | `/opt/Kingbase` 属主是 root | 执行 10.2 的 `chown` |
| 授权文件无效/服务无法启动 | license 缺失或与硬件不匹配 | 换官网开发版授权；安装前确认 `KB_LICENSE_PATH` |
| 安装器秒退、日志为空 | 静默安装异常 | 改用控制台向导（见 10.3） |
| 安装时想退出 | — | 提示符输入 `quit` 回车；或 `Ctrl+C`；许可协议页输入 `N` 也可退出 |

> 补充：官方支持静默安装（`sh setup.sh -i silent -f silent.cfg`，模板在 ISO 的 `setup/silent.cfg`），但本机实测静默模式返回 0 却未落盘任何文件，最终以控制台向导安装成功。若团队环境复现静默问题，建议反馈金仓官方。

## 11. 步骤八：启动 / 停止 / 验证

### 11.1 启动数据库（每次 WSL 启动后执行）

```bash
/opt/Kingbase/ES/V8/Server/bin/sys_ctl -w start -D /opt/Kingbase/ES/V8/data -l /opt/Kingbase/ES/V8/data/sys_log/startup.log
```

### 11.2 停止数据库

```bash
/opt/Kingbase/ES/V8/Server/bin/sys_ctl stop -m fast -w -D /opt/Kingbase/ES/V8/data
```

### 11.3 注册为系统服务（可选）

```bash
sudo /opt/Kingbase/ES/V8/install/script/root.sh
```

> 注意：当前 WSL 未启用 systemd，root.sh 注册的服务在 WSL 内不保证随开机自启，日常以 11.1 手动启动为准。

### 11.4 验证连接

WSL 内：

```bash
/opt/Kingbase/ES/V8/Server/bin/ksql -p 54321 -U system test
# 输入密码后执行
select version();
\q
```

Windows 应用侧（.NET / Kdbndp）直接连接：

```text
Host=localhost;Port=54321;Database=test;Username=system;Password=***
```

WSL2 默认支持 localhost 转发，Windows 无需额外配置。

## 12. 日常使用与运维

### 12.1 每次开机后的启动流程

```powershell
wsl                # 进入 Ubuntu（默认用户 kingbase）
```

```bash
# 启动金仓
/opt/Kingbase/ES/V8/Server/bin/sys_ctl -w start -D /opt/Kingbase/ES/V8/data -l /opt/Kingbase/ES/V8/data/sys_log/startup.log
```

可将快捷命令加入 `~/.bashrc`：

```bash
cat >> ~/.bashrc <<'EOF'
kbstart() { /opt/Kingbase/ES/V8/Server/bin/sys_ctl -w start -D /opt/Kingbase/ES/V8/data -l /opt/Kingbase/ES/V8/data/sys_log/startup.log; }
kbstop()  { /opt/Kingbase/ES/V8/Server/bin/sys_ctl stop -m fast -w -D /opt/Kingbase/ES/V8/data; }
kbstatus(){ /opt/Kingbase/ES/V8/Server/bin/sys_ctl status -D /opt/Kingbase/ES/V8/data; }
EOF
source ~/.bashrc
```

### 12.2 防止 WSL 空闲关机

- 保持一个 `wsl` 终端窗口常驻；或
- PowerShell 后台执行 `wsl -e sleep infinity`（保活）；或
- Windows 应用连接前先 `wsl -e true` 唤醒（若已关机，需重新启动金仓服务）。

### 12.3 查看端口与进程

```bash
ss -tlnp | grep 54321
ps -ef | grep kingbase
```

### 12.4 备份（建议每日）

```bash
/opt/Kingbase/ES/V8/Server/bin/kdb_dump -h localhost -p 54321 -U system -d test -f /home/kingbase/backup/test_$(date +%Y%m%d).sql
```

## 13. 注意事项汇总

1. **license 必填**：开发版授权从金仓官网下载中心获取（需登录），安装 SERVER 必须提供。
2. **启用功能后必须重启**：`VirtualMachinePlatform` 未重启生效前，WSL 命令会卡死。
3. **WSL 空闲自动关机**：挂载、服务都会随之丢失；安装和日常使用注意保活。
4. **locale 差异**：本机安装使用 `C` locale，字符集仍为 UTF8，业务无影响；如需中文 locale 先 `locale-gen`。
5. **大小写敏感**：ORACLE 兼容模式 + `CASE_SENSITIVE=YES`，SqlSugar 生成带引号标识符按原样存储，实体命名与建表风格需统一（项目统一小写下划线）。
6. **驱动版本**：金仓官方 NuGet 包 `Kdbndp` 当前版本 8.0.0；SqlSugar 枚举为 `DbType.Kdbndp`（不是 `DbType.Kingbase`）。
7. **首次 DropTable 报 42P01**：SqlSugar 在 Kingbase 上 `DbMaintenance.DropTable` 无 IF EXISTS 兜底，调用前先判断或 try/catch。
8. **档案与脚本**：ISO、授权文件、安装脚本等大文件一律放 `archive/`（已加入 .gitignore，不入库）。

## 14. 命令速查

| 场景 | 命令 |
|---|---|
| 进入 Ubuntu | `wsl` |
| 以 root 执行 | `wsl -u root -e bash -lc "<cmd>"` |
| 查看发行版 | `wsl -l -v` |
| 重启 WSL | `wsl --shutdown` |
| 挂载 ISO | `sudo mount -o loop /mnt/e/.../xxx.iso /mnt/kbiso` |
| 卸载 ISO | `sudo umount /mnt/kbiso` |
| 启动金仓 | `kbstart`（见 12.1） |
| 停止金仓 | `kbstop` |
| 连接测试 | `/opt/Kingbase/ES/V8/Server/bin/ksql -p 54321 -U system test` |
| MD5 校验 | `certutil -hashfile <文件> MD5` |
