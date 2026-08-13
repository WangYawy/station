# M27：前端代码分割 + ECharts 按需引入

> 状态：✅ 通过（2026-08-13，Spike 3/3）
> 目标：把平台前端从"单包 2.26MB"优化为"按路由懒加载 + 供应商分包 + 按需引入"，首页关键路径与统计页资源分离。

## 1. 交付内容

- **Element Plus 按需引入**：接入 `unplugin-auto-import` + `unplugin-vue-components`（`ElementPlusResolver`），移除全量 `app.use(ElementPlus)` 与全量样式；`ElMessage`/`ElMessageBox` 自动导入，生成 `auto-imports.d.ts` / `components.d.ts` 入库供类型检查；
- **路由懒加载**：登录页与五个业务视图全部改为动态 `import()`，首屏只加载当前页面；
- **手动分包**（`manualChunks`）：`vue-vendor` / `element-plus` / `echarts` 独立 chunk，长缓存友好；
- **ECharts 按需注册**：`echarts/core` + `BarChart` + `GridComponent` + `TooltipComponent` + `CanvasRenderer`，仅统计页（懒加载 chunk）触发加载；
- **图标显式引入**：侧边栏与头部仅引入用到的 6 个图标，移除全局注册全部图标；
- **结构整理**：`App.vue` 收敛为 `el-config-provider` 壳（中文 locale），原布局迁至 `layouts/AdminLayout.vue`；
- **构建脚本调整**：先 `vite build` 生成按需 dts，再 `vue-tsc` 类型检查（保证新克隆环境也可一次构建通过）。

## 2. 体积对比（minified）

| 资源 | M25（全量单包） | M27 |
|---|---|---|
| 入口 index-*.js | 2.26MB | 7.1KB |
| vue-vendor | - | 108KB（gzip 42KB） |
| element-plus（按需） | 包含在单包 | 911KB（gzip 295KB） |
| echarts | 包含在单包 | 445KB（gzip 149KB），仅统计页加载 |
| 各业务视图 | 包含在单包 | 1.5~5.6KB，路由懒加载 |

首屏关键路径约 370KB gzip（入口 + vue + element-plus + 样式），统计页首次打开时额外加载 echarts 149KB gzip。

## 3. 验证结果（Spike 3/3）

| 场景 | 结果 |
|---|---|
| 分块结构：入口 <100KB，echarts/vue-vendor/element-plus 分块存在，视图懒加载 | ✅ |
| 构建产物 17 个资源全部可访问（200） | ✅ |
| 登录 + `/api/v1/stats/overview` API 回归 | ✅ |

验证代码：[spikes/Station.Spike.PlatformWebChunks](../../spikes/Station.Spike.PlatformWebChunks/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.PlatformWebChunks -c Release
```

## 4. 说明与后续（M28 建议）

1. 真实 UMS/MTP 采集源 + SFTP 真实服务器联调（采集侧闭环收尾，需真实设备）；
2. 系统管理模块（组织架构/用户权限/日志管理）与记录仪管理页列 P1；
3. 统计/台账导出（CSV/Excel，按权限过滤并脱敏）列 P1；
4. 如需进一步瘦身，可对 element-plus 按需结果做组件级审计（当前已是按需 + 独立 chunk，满足 P0）。
