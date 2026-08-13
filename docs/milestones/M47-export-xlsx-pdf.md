# M47：台账/报表导出 xlsx / PDF

> 状态：✅ 通过（2026-08-13，Spike 9/9）
> 目标：P1 增强——在既有 CSV 导出基础上补齐 xlsx（Excel）与 PDF，前端导出按钮改为三格式下拉；顺带修复平台 MySQL 连接池泄漏。

## 1. 交付内容

### 导出构建器（`Station.Application/Exporting`）

- `ExportDocumentBuilder`：CSV（UTF-8 BOM + RFC4180，与旧行为一致）/ xlsx（MiniExcel，MIT）/ PDF（PDFsharp 6，MIT）三格式共享表头与行数据；PDF 为 A4 横向表格，表头加粗底纹、单元格自动换行、分页与页码；
- `SystemCjkFontResolver`：PDF 中文渲染的字体解析器——优先 `STATION__EXPORT__FONTFILE` 配置，否则探测系统字体（Windows：SimHei/微软雅黑等；Linux：wqy-microhei/Noto CJK/Droid），未找到时给出明确安装提示；
- 新增 NuGet：`MiniExcel 1.45.0`、`PDFsharp 6.2.4`（均 MIT，无商用授权风险，跨平台支持信创 Linux）。

### 接口

- 平台 7 个导出接口（文件/报警/采集站/记录仪/审计/趋势/排行）与单机 Web 2 个接口（文件/审计）统一支持 `format=csv|xlsx|pdf`（缺省 csv，非法值回退 csv）；
- 权限与数据范围不变（沿用各自接口的权限码 + 部门树过滤，审计导出 IP 脱敏）。

### 前端

- 平台 Web 7 处导出按钮 + 单机 Web 2 处导出按钮均改为下拉（CSV / Excel(xlsx) / PDF），下载文件名随格式变化。

### 附带修复：平台 MySQL 连接池泄漏

- 现象：Spike 连续请求时 MySQL `Threads_connected` 从 2 涨到 101（池满），后续请求报 "All pooled connections are in use"；
- 根因：平台共享 `SqlSugarScope` 单例在 async 线程切换下不释放连接；
- 修复：平台端 `ISqlSugarClient` 改为按请求创建独立客户端（auto-close，随请求作用域释放），`IDatabaseBackupService` 同步改 scoped 避免单例捕获请求级客户端；`SqlSugarFactory.CreateScope` 对非 SQLite 库恢复 auto-close（桌面 SQLite 保持常驻连接 + WAL 行为不变）。

## 2. 验证结果（Spike 9/9）

| 场景 | 结果 |
|---|---|
| 7 个导出接口 × csv/xlsx/pdf 三格式：魔数（BOM / PK / %PDF）+ xlsx 回读行数 + PDF 打开/嵌入字体/页数 | ✅（21 组全部 200） |
| 文件台账 / 审计日志样例 PDF：A4 横向、中文标题元数据、嵌入 `/FontFile`、Poppler 成功栅格化 | ✅ |
| 非法 format=doc 回退 CSV（BOM 前缀） | ✅ |
| 权限拦截：操作员导出审计（无 audit:export）→ 403 | ✅ |
| 连接池回归：连续 21 个请求后 MySQL Threads_connected 恒为 2（修复前升至 101） | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.PlatformExportP1 -c Release
```

## 3. 说明与后续

- Linux 部署（含信创容器）需在镜像安装 CJK 字体：`apt-get install -y fonts-wqy-microhei`（或 fonts-noto-cjk），或通过环境变量 `STATION__EXPORT__FONTFILE` 指定字体文件；Docker Compose 服务清单 P0 文档需补充该依赖；
- 单机 Web 前端源码位于 `station-web/`（独立构建输出到桌面端 wwwroot），已同步支持三格式；
- 剩余 P1：SignalR 实时推送、MTP 采集源。
