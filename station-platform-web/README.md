# 监控管理平台 Web（station-platform-web）

平台端前端工程：Vue 3 + TypeScript + Vite + Element Plus + ECharts，对应后端 `Station.Platform.Api`（端口 5100）。

## 开发

```bash
npm install
npm run dev
```

开发服务器默认 `http://localhost:5173`，`/api` 代理到 `http://127.0.0.1:5100`（后端需先启动）。

## 构建

```bash
npm run build
```

构建产物直接输出到 `src/Station.Platform/Station.Platform.Api/wwwroot`，由平台后端静态托管（哈希路由，深链无需后端回退配置）。

默认账号：`admin / Admin@123`。
