# M49：系统设置（单机 Web + 桌面端模块）

> 状态：✅ 通过（2026-08-14，Station.Spike.Settings 14/14）
> 目标：按单机版 Web 后台原型"设置"页六类分组（基本/存储/授权/采集/网络/自检），实现单机 Web 系统设置页与桌面端设置模块；仅管理员或授权用户（setting:manage）可修改。

## 1. 设置模型与持久化

### 分组与字段（对齐原型）

| 分组 | 字段 |
|---|---|
| 基本设置 | 本机编号（只读）、安装位置、运行模式（单机版/平台版）、平台地址、平台站点编号 |
| 存储策略 | 存储目标（本地/FTP/SFTP）、连接参数（主机/端口/账号/密码 SM4 加密）、目录模板、熔断阈值/冷却、视频保留天数、日志保留天数、清理执行时间 |
| 采集策略 | 接入自动采集、采集后擦除、跳过已采集、采集附属文件（日志/图片/音频） |
| 网络安全 | Web 端口、HTTPS 端口、局域网访问、允许 HTTP、HTTPS 证书状态/上传、登录锁定（次数/分钟） |
| 授权与激活 | 授权状态（正式/试用/到期）、离线激活（station.lic） |
| 设备自检 | 一键自检（采集设备/磁盘读写/网络/存储目标）+ PDF/xlsx/CSV 报告导出 |

### 运行时持久化（appsettings.runtime.json）

- 新增 `IRuntimeSettingsFile`（共享抽象）+ `RuntimeSettingsFile`（桌面基础设施实现，路径可用 `STATION__RUNTIMESETTINGSPATH` 覆盖）；
- `HostBuilderFactory` 在 appsettings.json 之后加载运行时文件（含 WebHost 启动用的配置），重启后设置生效；
- 修改时热应用到选项单例（采集/登录锁定/平台开关即时生效），并整体写回运行时文件；Web 端口/证书/存储目标等提示"重启后生效"；
- 修复 .NET 配置绑定对 `List<string>` 追加而非替换的问题：`CollectOptions.FileExtensions` 按配置整体重建，避免与默认白名单叠加；
- FTP/SFTP 密码以 SM4 密文（`sm4:` 前缀）落盘。

## 2. 服务端

- `Station.Application/Settings`：`StationOptions`（Station:Basic）、`SystemSettingsService`（Get/Update + 审计）、`ISystemSettingsService`；
- `Station.Desktop.Application/Settings`：`SystemSelfCheckService`（设备/磁盘/网络/存储目标四项自检）；
- `Station.Desktop.WebHost/Settings`：`NetworkSettingsService`（Web/HTTPS/证书/登录锁定，证书保存到本地数据目录并启用 HTTPS）；
- `SettingsController`：GET 设置（setting:view）、PUT 分组修改（setting:manage）、授权激活（文本体）、证书上传、自检执行、自检报告导出；每次修改/激活/自检写审计；平台版（runMode=platform）本地只读返回 403 拦截。

## 3. 前端

### 单机版 Web（station-web）

- 新增 `SettingsView.vue`：左侧六类导航 + 分组表单（与原型一致），编辑控件按 `setting:manage` 与只读模式禁用；授权激活粘贴 station.lic；自检结果表格 + 报告下载下拉；
- 路由 `/settings` + 侧边栏菜单（`setting:view` 可见）。

### 桌面端（Avalonia）

- 新增 `SettingsModuleView` + `SettingsModuleViewModel`：Tab 式六分组（基本/存储/采集/网络/授权/自检），与 Web 共享同一套服务；
- ShellWindow"设置"导航接入真实模块（原为占位页）；进入需登录 + `setting:view`，编辑按钮按 `setting:manage` 禁用；
- 证书上传入口引导至单机 Web 后台。

## 4. 权限

- 查看：`setting:view`（管理员/部门负责人预置）；
- 修改：`setting:manage`（仅管理员预置；自定义角色可授权）；
- 平台版：配置由平台统一下发，本地只读（接口 403 + 界面提示）。

## 5. 验证（Station.Spike.Settings 14/14）

| 场景 | 结果 |
|---|---|
| 设置读取：六分组齐全（含授权状态） | ✅ |
| 采集策略修改 → 热应用（AutoCollect/附属文件立即生效） | ✅ |
| 存储/网络修改 → 返回"重启后生效"提示 | ✅ |
| 基本设置切平台版 → readOnly=true；再修改被 403 拦截 | ✅ |
| 运行时文件落盘（视频白名单、Web 端口等） | ✅ |
| 重启后设置持久化（webPort=5126、附属文件关闭、平台版只读、登录锁定 3 次） | ✅ |
| 设备自检 4 项 + PDF 报告导出（%PDF 魔数） | ✅ |
| 部门负责人：GET 200 / PUT 403；操作员：GET 403 | ✅ |
| 授权激活无效文件明确报错 | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.Settings -c Release
```

## 6. 附带修复

- 六个后台托管服务（平台同步/定时采集/缓存清理/备份/上传/台账上报）关闭时 `Task.Delay` 取消异常不再输出 fail 日志，重启/关机干净退出。

## 7. 后续

- HTTPS 证书上传/安装的浏览器端 E2E 验证（需 PFX 样例）；
- 平台版"由平台统一下发"与本地设置页只读的联调（ConfigApplyService 已覆盖采集/存储策略）。
