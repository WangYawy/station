# M6：桌面端操作台 UI（按原型重做）+ 弹窗登录 + 会话保持 + 权限导航

> 状态：✅ 完成（2026-08-12，桌面端可运行冒烟通过）
> 依据：`docs/prototypes/采集站-桌面端3.html`（操作端定位，非后台管理风格）

## 1. 原型还原要点

原型"采集站-桌面端3.html"的定位是**采集操作台**而非后台管理：

- 顶部深蓝导航条：Logo + 标题"采集站" + 居中**药丸菜单**（工作台/采集作业/历史记录/日志中心/设置）+ 右侧"正式版"徽标与登录状态；
- 主区为**工作台仪表盘**：统计卡片（在线设备/今日采集/待上传/本机状态）、📡 设备连接池、📋 采集队列（当前任务）及状态筛选标签；
- 底部状态栏：本机 IP、网络/心跳状态、时间；
- **登录是操作时弹出的模态窗**（"请验证身份以继续操作"），支持 密码/指纹/人脸 三种方式（P0 仅密码，指纹/人脸页签禁用占位）；
- 空闲超时前出现**"即将自动退出登录，请操作以保持会话"**提示条，有操作自动延长。

## 2. 交互流程（关键变更）

| 场景 | 行为 |
|---|---|
| 启动 | 直接进入工作台（未登录状态，顶部显示"未登录"），**不再先弹登录页** |
| 点击需登录的导航/操作（默认全部模块） | 未登录 → 弹出登录对话框；取消则停留原页 |
| 登录成功 | 对话框关闭，进入目标模块；顶部显示 用户名（账号）+ "退出登录" |
| 登录后权限校验 | 无该模块权限点 → 显示"无权限访问"占位页 |
| 空闲超时 | 最后 10 秒显示黄色警告条（剩余秒数）；超时自动退出登录，回到"未登录"状态 |
| 注销 | 写登出审计，回到未登录状态 |

## 3. 交付内容

### 桌面应用层（Station.Desktop.Application）

- `ISessionManager`：会话状态 + 事件（登录/登出/超时）+ `HasPermission`
- `IOperationAccessService` + `OperationAuthOptions`：**操作权限配置**（`Station:OpAuth:RequiredModules`），默认全部模块需登录（符合需求基线），可按原型默认调整为仅"设置"需登录
- `AuthSeedHostedService`：启动自动种子

### UI 层（Station.Desktop.UI）

- `ShellWindow`：顶部导航条 + 空闲警告条 + 模块内容区 + 底部状态栏（时钟）
- `WorkbenchView`：统计卡片 + 设备连接池 + 采集队列（M6 静态占位，M7 接真实服务）
- `ModulePlaceholderView`：各模块占位页 / 无权限页
- `LoginDialog`：模态登录窗（密码页签可用；指纹/人脸页签禁用占位），回车登录、失败原因提示、锁定倒计时提示
- `SessionIdleTracker`：每秒倒计时，最后 10 秒发警告事件，超时自动退出
- 导航选中态药丸高亮（`nav-pill-selected` 样式）

## 4. 验证情况

- `Station.Desktop.sln` Release 构建 0 警告 0 错误；
- 冒烟启动通过（进程存活、`station.db` 自动创建）；
- 手动验证路径：启动 → 工作台（未登录）→ 点"设置" → 弹登录窗 → 输错 5 次看锁定提示 → `admin`/`Admin@123` 登录 → 进入设置占位页 → 1 分钟不动看警告条并自动退出。

```powershell
dotnet run --project src/Station.Desktop/Station.Desktop.UI
```

## 5. 配置

```json
{
  "Station": {
    "Db": { "Provider": "Sqlite", "ConnectionString": "Data Source=station.db" },
    "Auth": { "MaxFailedAttempts": 5, "LockoutMinutes": 15, "AutoLogoutMinutes": 1 },
    "OpAuth": { "RequiredModules": [ "workbench", "collect", "history", "logs", "settings" ] }
  }
}
```

## 6. 后续（M7 建议）

- 工作台/设备连接池/采集队列接真实服务（设备管理、任务状态）；
- 采集作业模块：记录仪接入识别、任务列表与操作按钮（开始/暂停/擦除，按权限启用）；
- 操作权限管理页（设置内）可视化编辑 `RequiredModules`；
- 单机版内置 Web 登录 API + Cookie 认证 + RBAC 中间件。
