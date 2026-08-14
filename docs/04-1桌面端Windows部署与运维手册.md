# 采集站桌面端 Windows 部署与运维手册

> 适用对象：实施/运维人员。本册只讲 Windows 桌面端；总纲见
> 《部署与运维手册》（04），Linux/麒麟/统信桌面端见《桌面端 Linux 麒麟统信部署与运维手册》（04-2）。

## 一、你需要准备的东西

| 物品 | 说明 |
| :--- | :--- |
| Windows 电脑 | Windows 10 / 11 64 位（x64） |
| 安装包 | `Station.Desktop-<版本>-win-x64.msi`（找开发/交付人员要，或从 CI 发布下载） |
| 管理员账号 | 安装软件、注册任务计划需要管理员权限 |
| 现场信息 | 采集源模式（真机 UMS / 模拟）、存储方式（本地/FTP/SFTP）、平台地址与站编号（平台版才需要） |
| 授权文件 | 正式授权由内部工具生成（可选，试用期可不装） |

> 说明：桌面端默认使用内置 SQLite 数据库，**不需要安装任何数据库软件**。

## 二、部署前检查（10 分钟）

右键"开始"菜单 → 选择 **Windows PowerShell（管理员）**，逐条复制执行：

### 2.1 检查系统版本与架构

```powershell
(Get-CimInstance Win32_OperatingSystem) | Select-Object Caption, Version, OSArchitecture
$env:PROCESSOR_ARCHITECTURE
```

✅ 预期结果：显示 Windows 10/11，架构为 `AMD64`。如果是 ARM64 或 32 位，请先联系交付人员。

### 2.2 确认管理员权限

```powershell
([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
```

✅ 预期结果：`True`。如果是 `False`，请以管理员身份重新打开 PowerShell。

### 2.3 检查磁盘与内存

```powershell
Get-PSDrive C | Select-Object @{n='剩余GB';e={[math]::Round($_.Free/1GB,1)}}
[math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory/1GB,1)
```

✅ 预期结果：剩余磁盘 ≥ 20GB、内存 ≥ 4GB。不足请先清理或联系管理员。

### 2.4 检查端口占用

```powershell
Get-NetTCPConnection -LocalPort 5000,5100 -ErrorAction SilentlyContinue
```

✅ 预期结果：**没有任何输出**（端口空闲）。有输出说明 5000/5100 被占用，需先关闭占用程序或换端口。

## 三、安装（推荐：MSI 安装包）

### 3.1 图形界面安装（新手推荐）

1. 双击 `Station.Desktop-0.1.0-win-x64.msi`；
2. 弹出向导后一路点 **下一步**，直到 **完成**；
3. 开始菜单出现 **Station Desktop** 快捷方式。

✅ 安装完成后检查：

```powershell
Test-Path "C:\Program Files\Station Desktop\Station.Desktop.UI.exe"
```

预期输出 `True`。

### 3.2 静默安装（批量部署用）

```powershell
msiexec /i Station.Desktop-0.1.0-win-x64.msi /qn /l*v install.log
```

安装过程无界面，日志写入 `install.log`；出问题把日志发给开发人员。

### 3.3 升级

直接运行新版安装包即可（旧版本自动覆盖，配置不会被覆盖）：

```powershell
msiexec /i Station.Desktop-0.2.0-win-x64.msi /qn
```

### 3.4 卸载

```powershell
msiexec /x Station.Desktop-0.1.0-win-x64.msi
```

或在"设置 → 应用"里卸载 Station Desktop。

### 3.5 内网 / 无网络现场提示（先读）

- 本程序为**自包含发布**：安装和运行全程**不需要联网**，也不会联网下载任何组件；
- 安装包 `Station.Desktop-0.1.0-win-x64.msi` 和授权文件提前在有网机器拿到，
  通过 U 盘/内网共享拷到目标机即可安装；
- 平台版对接的是**内网平台地址**（`BaseUrl` 填内网 IP），不依赖外网；
- 现场不要执行任何"在线更新/下载组件"类操作，直接按第三章安装；
- 授权激活、平台注册、采集上传全部在内网完成，无网络不影响。

## 四、拷贝部署（没有安装包时）

1. 找开发/交付人员要发布目录 `publish/desktop/win-x64/`（或自己构建）；
2. 把目录里所有内容拷贝到目标机，例如 `C:\Station\Desktop\`：

   ```powershell
   Copy-Item -Path .\win-x64\* -Destination C:\Station\Desktop -Recurse -Force
   ```

3. 双击 `C:\Station\Desktop\Station.Desktop.UI.exe` 启动。

> 建议固定放在无空格的目录（如 `C:\Station\Desktop`），避免路径问题。

## 五、配置（appsettings.json）

程序默认按内置配置运行，通常**只需在平台版或改存储时才需要编辑**。

1. 进入安装目录（`C:\Program Files\Station Desktop\` 或拷贝目录）；
2. 若已有 `appsettings.json` 用记事本打开；若没有，新建文件命名为 `appsettings.json`；
3. 复制下面内容，按现场修改后保存（注意保存为 UTF-8 编码）：

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

各项含义：

| 配置项 | 填什么 |
| :--- | :--- |
| `Collect:SourceMode` | `ums` 接真机采集；`simulated` 开发测试用 |
| `Storage:Target` | `Local` 存本机；`Ftp` / `Sftp` 传远端（需再配主机/端口/账号） |
| `Platform:Enabled` | `false` 单机版；`true` 平台版（再填 `BaseUrl` 和 `StationCode`） |
| `Command:PublicKeyPem` | 平台下发的验签公钥（平台版指令验签时填，由交付人员提供） |

改完**重启程序**生效。

## 六、首次启动与验证

1. 双击开始菜单 **Station Desktop** 启动；
2. 界面出现后，确认右上角/设置里的**授权状态**（试用 N 天或已激活）；
3. 单机版：本机浏览器打开 `http://127.0.0.1:5000`，能看到内置 Web 界面；
4. 平台版：确认配置了平台地址后，登录平台后台，台账里能看到该站"在线"。

✅ 都正常后，插一台记录仪 U 盘做一次真实采集冒烟（自动识别 → 采集 → 校验）。

## 七、开机自启（可选）

```powershell
schtasks /Create /TN "StationDesktop" /TR "C:\Program Files\Station Desktop\Station.Desktop.UI.exe" /SC ONLOGON /RL LIMITED /F
```

取消自启：

```powershell
schtasks /Delete /TN "StationDesktop" /F
```

## 八、日常运维

### 8.1 数据与日志位置

| 内容 | 位置 |
| :--- | :--- |
| 本地数据库 | 进程工作目录下的 `station.db`（一般即安装目录） |
| 采集文件 | `Station:Storage:LocalRoot` 指定目录（默认相对工作目录） |
| 日志 | 程序目录 `logs/`（Serilog 按天滚动） |

### 8.2 备份（建议每周）

1. 关闭桌面端程序；
2. 把安装目录下的 `station.db` 和采集文件目录整体复制到备份盘或服务器；
3. 恢复时把备份文件放回原目录即可。

### 8.3 升级

1. 备份（见 8.2）；
2. 运行新版 MSI 覆盖安装（或替换拷贝目录，保留 `appsettings.json`）；
3. 启动验证（见第六章）。

## 九、常见问题

| 现象 | 处理 |
| :--- | :--- |
| 双击没反应 | 确认是 64 位系统；看程序目录 `logs/` 最新日志；或管理员 PowerShell 运行 `.\Station.Desktop.UI.exe` 看报错 |
| 单机版网页打不开 | 确认程序已启动；浏览器访问 `http://127.0.0.1:5000`（本机）；局域网访问需防火墙放行 5000 |
| 授权显示未激活/已过期 | 按《部署与运维手册》第八章做授权激活；到期瞬间会中断采集 |
| 接 U 盘不识别 | 确认 `SourceMode=ums`；换 USB 口；检查设备是否被系统识别 |
| SFTP 上传失败 | 核对 SFTP 地址/端口/账号，确认远端目录可写 |
| 平台不上线 | 确认 `Platform:Enabled=true`、`BaseUrl` 可达（`ping`/浏览器打开）、`StationCode` 与平台一致 |
| 内网/无网络环境 | 程序自包含、不需要联网；把 MSI 与授权文件提前拷入，按 3.5/第三章直接安装 |

## 十、交付自检清单

- [ ] 系统检查通过（版本/架构/权限/磁盘/端口）
- [ ] 安装完成，`Station.Desktop.UI.exe` 存在
- [ ] 程序启动正常，授权状态确认
- [ ] 单机版 Web（5000）或平台版上线验证通过
- [ ] 真机采集冒烟通过（如现场有设备）
- [ ] 开机自启已配置（如需）
- [ ] 备份策略已告知客户（`station.db` + 采集文件）
- [ ] 无网络现场：安装包/授权文件已提前拷入并完成离线安装验证
