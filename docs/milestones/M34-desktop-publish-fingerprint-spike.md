# M34：桌面端跨平台发布（信创 x86_64/ARM64 单文件）+ Linux 机器指纹

> 状态：✅ 通过（2026-08-13，指纹 Spike 4/4；win-x64 / linux-x64 / linux-arm64 单文件发布成功）
> 目标：交付准备第一步——桌面端可在 Windows 与信创 Linux（海光/兆芯 x86_64、飞腾/鲲鹏 ARM64）上以单文件运行，且机器指纹跨平台可采集。

## 1. 交付内容

**机器指纹跨平台**

- [IMachineFingerprintProvider](../../src/Station.Shared/Station.Infrastructure/Licensing/IMachineFingerprintProvider.cs) 重构：`CollectParts()` 返回 CPU/主板/磁盘/MAC 原始四字段，`CollectFingerprint()` = `SM3(ToRaw())`（默认接口实现，授权绑定语义不变）；
- [WindowsMachineFingerprintProvider](../../src/Station.Shared/Station.Infrastructure/Licensing/WindowsMachineFingerprintProvider.cs)：WMI 采集四字段（原实现仅返回哈希，无法用于注册上报）；
- [LinuxMachineFingerprintProvider](../../src/Station.Shared/Station.Infrastructure/Licensing/LinuxMachineFingerprintProvider.cs)：sysfs 读取（DMI `product_uuid`/`board_serial` + `/sys/block/*/device/serial` + MAC），自动排除 loop/ram/光驱，缺失或无权限回退 `unknown`；`sysfsRoot` 可注入便于测试；
- DI 按操作系统选择指纹提供者（`OperatingSystem.IsWindows()`）；
- **平台注册上报真实机器指纹**（此前硬编码 `unknown`），采集站台账可看到真实硬件序列号。

**跨平台单文件发布**

- [publish-desktop.ps1](../../scripts/publish-desktop.ps1)：`dotnet publish` self-contained + PublishSingleFile，默认输出 win-x64 / linux-x64 / linux-arm64 三平台。

## 2. 验证结果

| 场景 | 结果 |
|---|---|
| Windows 指纹：WMI 真实 CPU/主板/磁盘/MAC，SM3 指纹两次一致且等于 SM3(ToRaw()) | ✅ |
| Linux 指纹：sysfs 模拟根正确读取，loop/ram 排除，SM3 一致 | ✅ |
| Linux 缺失/无权限回退 unknown | ✅ |
| DI 按 OS 选择（当前 Windows → WindowsMachineFingerprintProvider） | ✅ |
| win-x64 / linux-x64 / linux-arm64 单文件发布成功（141~151MB） | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.Fingerprint -c Release
powershell -File scripts/publish-desktop.ps1
```

## 3. 说明与后续（M35 建议）

- Linux 运行时依赖 Avalonia 原生库（libX11/libxcb/fontconfig 等，麒麟/统信通常自带），部署文档中列出；
- 真机验证：在信创主机运行 linux-x64/linux-arm64 产物并完成授权激活（授权绑定新指纹）；
- M35：平台 Docker Compose 交付包（后端 + MySQL/PostgreSQL/Kingbase 切换 + HTTPS 说明）；
- M36：部署与运维文档（授权激活流程、升级、备份恢复）。
