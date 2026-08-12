# M6：桌面端登录 UI + 会话保持 + 权限导航

> 状态：✅ 完成（2026-08-12，桌面端可运行冒烟通过）
> 目标：把 M5 的认证/RBAC 服务接到 Avalonia 桌面端：登录界面、会话管理、1 分钟无操作自动退出、导航按权限显隐。

## 1. 交付内容

### 桌面应用层（Station.Desktop.Application）

| 组件 | 说明 |
|---|---|
| `ISessionManager` / `SessionManager` | 当前会话（AuthSession）、`SessionChanged`/`SessionExpired` 事件、`HasPermission`（admin 角色放行） |

### 桌面基础设施（Station.Desktop.Infrastructure）

| 组件 | 说明 |
|---|---|
| `AuthSeedHostedService` | 应用启动时自动执行认证种子（幂等），登录前保证 admin 账号/角色就绪 |

### UI 层（Station.Desktop.UI）

| 组件 | 说明 |
|---|---|
| `LoginView` + `LoginViewModel` | 账号/密码/错误提示/锁定提示，回车登录；失败原因映射（禁用/锁定/凭证错误） |
| `MainView` + `MainWindowViewModel` | 左侧权限导航 + 顶部用户信息/注销；导航项按 `HasPermission` 过滤（admin 全显） |
| `ShellWindow` | 壳窗口：登录页 ⇄ 主界面切换（会话事件驱动） |
| `SessionIdleTracker` | 键盘/鼠标输入重置计时，无操作达 `AutoLogoutMinutes`（默认 1 分钟）自动退出登录 |

## 2. 验证情况

- `Station.Desktop.sln` Release 构建 0 警告 0 错误；
- 应用启动冒烟：进程存活、`station.db` 自动创建（种子落库）、登录窗口正常显示；
- 登录账号：`admin` / `Admin@123`（可在 `Station:Auth` 配置调整）。

```powershell
dotnet run --project src/Station.Desktop/Station.Desktop.UI
```

## 3. 关键行为

1. **登录**：成功 → 写入会话 → 切换到主界面；失败 5 次 → 锁定 15 分钟，界面提示锁定时间。
2. **权限导航**：导航目录（工作台/文件台账/报警/审计/用户/部门/角色/设置）按当前会话权限过滤，无权限项不显示。
3. **无操作自动退出**：`Station:Auth:AutoLogoutMinutes`（默认 1），任意键盘/鼠标输入重置；超时 `SessionManager.Clear()` → 回到登录页（本次实现不区分"手动注销"与"超时退出"的提示文案，待 M7 细化）。
4. **注销**：写登出审计后清除会话。

## 4. 配置示例

```json
{
  "Station": {
    "Db": {
      "Provider": "Sqlite",
      "ConnectionString": "Data Source=station.db"
    },
    "Auth": {
      "MaxFailedAttempts": 5,
      "LockoutMinutes": 15,
      "AutoLogoutMinutes": 1
    }
  }
}
```

## 5. 后续（M7 建议）

- 各导航模块真实页面（文件台账、报警、审计、用户/部门/角色管理）；
- 免登录模式配置与"操作人"确定（记录仪绑定/手选）；
- 单机版内置 Web 登录 API + Cookie 认证 + RBAC 中间件；
- 会话超时/手动注销区分提示，空闲倒计时展示。
