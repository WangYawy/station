# M25：平台前端 Vue 工程化（station-platform-web）

> 状态：✅ 通过（2026-08-13，Spike 4/4）
> 目标：把平台端最小单文件 HTML 前端升级为可扩展的 Vue3 + TypeScript 工程，与后端现有 API 对齐交付完整页面，构建产物由平台后端静态托管。

## 1. 交付内容

- **新前端工程 `station-platform-web`**：Vue 3.5 + TypeScript 5.8 + Vite 6 + Vue Router（hash 路由）+ Pinia + Element Plus + ECharts；
- **页面**：
  - 登录（Cookie 会话，401/密码错误提示）；
  - 总览驾驶舱：统计卡片（站/在线率/文件/容量/今日采集/待处理报警）+ 采集趋势 ECharts 柱状图 + 采集排行 + 报警统计；
  - 文件检索：关键字/类型/部门筛选、分页、H.264 在线预览（H.265 提示下载，转码 P2）；
  - 报警中心：级别/状态筛选、确认/处理/关闭（按 `alert:handle` 显隐）；
  - 采集站管理：授权状态标签、部门归属下拉调整（按 `station:manage` 显隐）；
  - 远程指令：指令下发 + 指令列表；
- **权限联动**：菜单与操作按钮按会话权限显隐，接口 401 自动回登录页；
- **工程化**：`npm run dev`（5173，`/api` 代理到后端 5100）；`npm run build` 经 `vue-tsc` 类型检查后输出到平台 `wwwroot`，哈希路由深链无需后端 SPA 回退配置；
- **后端零改动**：平台继续 `UseDefaultFiles + UseStaticFiles` 托管，构建产物即部署物。

## 2. 验证结果（Spike 4/4）

| 场景 | 结果 |
|---|---|
| `/` 返回 Vue 构建入口（`#app` + `/assets/index-*.js`） | ✅ |
| 构建 JS 资源可访问（200） | ✅ |
| 登录（admin）+ `/api/v1/stats/overview` 200 | ✅ |
| 未登录访问 `/api/v1/files` → 401 | ✅ |

验证代码：[spikes/Station.Spike.PlatformWeb](../../spikes/Station.Spike.PlatformWeb/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.PlatformWeb -c Release
```

前端工程：[station-platform-web](../../station-platform-web/README.md)

## 3. 说明与后续（M26 建议）

1. 真实 UMS/MTP 采集源 + SFTP 真实服务器联调（采集侧闭环收尾）；
2. 远程指令 SM2 验签升级为 PlatformCommand 读取时实时签名校验（当前为下发时生成签名入库存证）；
3. 前端按路由代码分割 + ECharts 按需引入（当前单包约 2.2MB）；
4. 系统管理模块（组织架构/用户权限/日志管理）与记录仪管理页列 P1；
5. 统计/台账导出（CSV/Excel，按权限过滤并脱敏）列 P1。
