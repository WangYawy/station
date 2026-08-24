# M56：Linux 真实 MTP 采集源（libmtp）

> 状态：✅ 通过（2026-08-24，WSL Ubuntu 实测 spike 6/6；双端全量构建 + linux-x64 发布）
> 目标：Linux（信创）MTP 从"不支持/模拟"改为真实设备模式——通过 libmtp 枚举、扫描、下载、擦除与绑定文件读写，并与 UMS 一起参与混合协议路由。

## 1. LinuxMtpCollectSource（libmtp P/Invoke）

- 依赖 libmtp9（Ubuntu/Kylin/UOS：`apt install libmtp9 libmtp-common`），运行时按 `libmtp.so.9 → .so.10 → .so → libmtp` 解析，缺失时明确报错"未安装 libmtp"；
- 结构体布局与函数签名按 libmtp 1.1.19 头文件逐一核对（`LIBMTP_mtpdevice_t`/`LIBMTP_file_t`/`LIBMTP_devicestorage_t`，进度回调 `LIBMTP_progressfunc_t`）；
- 设备枚举：`LIBMTP_Get_Connected_Devices`，键 = 序列号（缺失时友好名/型号），虚拟根 `MTP://{key}` 与 Windows 一致；
- 扫描：`LIBMTP_Get_Storage` + `LIBMTP_Get_Files_And_Folders` 递归，文件夹按 filetype=FOLDER 区分，文件路径编码 `{key}\u001F{对象ID}`；
- 下载：`LIBMTP_Get_File_To_File`（托管进度回调，支持取消）；
- 擦除：递归收集白名单文件 → `LIBMTP_Delete_Object`（普通删除）；
- 绑定文件：根目录读（Get_File_To_File）、写（`LIBMTP_new_file_t` + `LIBMTP_Set_File_Name` + `LIBMTP_Send_File_From_File`）、删（Delete_Object），实现 `IRecorderRootFileStore`。

## 2. 接入既有混合路由

- `MtpDeviceDetector`：Linux 分支走 libmtp 枚举（`MtpDeviceFilter` 生效），Windows 保持 WPD；
- `CollectSourceProvider`：Linux 上 MTP → `LinuxMtpCollectSource`（UMS/MTP 混合接入逐台路由）；
- 桌面 DI：Linux 注册 LinuxMtpCollectSource + MTP 复合绑定存储（`CompositeRecorderRootFileStore` 泛化为接收任意 MTP 根存取实现）；
- `RecorderConnectMonitor` 真实模式下同时监听 UMS + MTP，Linux MTP 设备接入自动识别、绑定校验、自动采集。

## 3. 验证（WSL Ubuntu 实测）

| 场景 | 结果 |
|---|---|
| libmtp 加载与设备枚举（无设备=0，不抛异常） | ✅ |
| 无设备：扫描/根目录/绑定读取均明确报"未检测到 MTP 设备" | ✅ |
| 检测器 Linux 分支走 libmtp | ✅ |
| Provider 路由 MTP → LinuxMtpCollectSource | ✅ |
| 双端全量构建 + linux-x64 发布 + DesktopAudit 17/17 回归 | ✅ |

```bash
# 目标机安装依赖
sudo apt-get install -y libmtp9 libmtp-common
# spike（Linux 上执行）
dotnet publish spikes/Station.Spike.MtpLinux -c Release -r linux-x64 --self-contained true
```

## 4. 说明

- 无真机环境下验证到"无设备明确报错 + 路由正确"；真机接入后扫描/下载/擦除/绑定走同一代码路径；
- 记录仪绑定：UMS 与 MTP 统一按 `MTP://`/盘符根经 `IRecorderRootFileStore` 读写 `station_bind.ini`，识别流程无平台差异。
