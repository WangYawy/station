# 监控管理平台 Web（station-platform-web）

平台端前端工程：Vue 3 + TypeScript + Vite + Element Plus + ECharts，对应后端 `Station.Platform.Api`（端口 5100）。

属于仓库根 **pnpm workspace** 的一员（公共代码见 `packages/shared`，即 `@station/shared`），依赖在仓库根一次性安装。

## 开发

```bash
# 仓库根执行一次
pnpm install

# 本工程开发（仓库根执行，或 cd 进本目录后 pnpm run dev）
pnpm --filter station-platform-web dev
```

开发服务器默认 `http://localhost:5173`，`/api` 代理到 `http://127.0.0.1:5100`（后端需先启动）。

## 构建

```bash
pnpm --filter station-platform-web build
```

构建产物直接输出到 `src/Station.Platform/Station.Platform.Api/wwwroot`，由平台后端静态托管（哈希路由，深链无需后端回退配置）。

默认账号：`admin / Admin@123`。
