# M13：平台文件统一列表/检索/预览（采集站流端点 + 平台代理 + 最小前端）

> 状态：✅ 通过（2026-08-13，全链路 6/6）
> 目标：基于 M12 上报的平台元数据提供统一列表/检索，并打通"平台前端 → 平台预览代理 → 采集站内置 Web 文件流"的 H.264 在线预览链路（H.265 引导下载）。

## 1. 交付内容

### 契约

- `StationRegistrationRequest` 增加 `StationBaseUrl`（采集站内置 Web 地址，注册时上报，供预览代理）；
- `FileMetadataView`：平台文件列表查询 DTO。

### 采集站（Station.Desktop.WebHost）

- `FileStreamController`：`GET /api/v1/files/{fileNo}/stream` —— 按 FileNo 查本地缓存，`PhysicalFile` 支持 **Range（206 分段）**，按扩展名返回 MIME（H.264 可播）。默认仅本机访问（局域网开启 + 登录认证后续里程碑）。

### 平台端（Station.Platform.Api）

- `PlatformFilesController`：
  - `GET /api/v1/files`：统一列表/检索（站/关键字/类型/时间范围 + 分页）；
  - `GET /api/v1/files/{fileNo}`：详情；
  - `GET /api/v1/files/{fileNo}/preview`：**预览代理**——查采集站注册地址，转发到采集站流端点并**透传 Range/Content-Range/206**；
- `PlatformStation` 增加 `StationBaseUrl`（注册落库）；
- 最小前端页 `wwwroot/index.html`：文件列表表格 + 关键字/类型筛选 + 分页 + 视频预览（`<video src=.../preview>`，H.265 提示下载）。

## 2. 验证结果（全链路 6/6）

| 步骤 | 结果 |
|---|---|
| 平台自动注册（StationBaseUrl 上报） | ✅ |
| 平台文件统一列表（keyword 检索总数 3） | ✅ |
| 采集站内置 Web 文件流（真实文件字节一致 2MB） | ✅ |
| 平台预览代理转发（200 + 字节一致） | ✅ |
| Range 分段（206 + 100 字节） | ✅ |
| 清理 | ✅ |

验证代码：[spikes/Station.Spike.PlatformPreview](../../spikes/Station.Spike.PlatformPreview/Program.cs)（迷你桌面端宿主 + 真实平台 API + MySQL）

```powershell
dotnet run --project spikes/Station.Spike.PlatformPreview -c Release
```

## 3. 联调入口

```powershell
dotnet run --project src/Station.Platform/Station.Platform.Api -c Release
# 浏览器打开 http://127.0.0.1:5100/ （平台文件列表页）
# 桌面端 Station:Platform.Enabled=true 后注册上报即出现在列表；点"预览"播放 H.264
```

## 4. 说明与后续（M14 建议）

1. 预览链路 P0 保证 H.264；H.265 前端提示下载（转码 P2）；
2. 采集站流端点当前默认本机访问，开启局域网后需登录认证 + 数据范围过滤；
3. 平台前端为最小静态页，正式 UI 待 Vue 工程化（station-platform-web）；
4. 后续：平台报警/指令管理页、配置同步应用、真实 UMS/MTP 源与 SFTP 联调收尾。
