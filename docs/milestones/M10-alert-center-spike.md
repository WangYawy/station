# M10：报警中心模块（列表/筛选/状态流转 + 权限 + 新报警提示）

> 状态：✅ 通过（2026-08-13，SQLite + Kingbase 4/4）
> 目标：把 M9 及后续产生的报警可视化闭环：列表筛选、确认/处理/关闭、权限控制、新报警即时提示（P0 界面；声音/弹窗/邮件短信 P1）。

## 1. 交付内容

### 应用层（Station.Application.Alerts）

- `IAlertService` 扩展：
  - `GetAlertsAsync(level, status, count)`：按级别/状态筛选、时间倒序分页；
  - `CountPendingAsync()`：待处理计数（顶部横幅/工作台）；
  - `SetStatusAsync(id, status)`：状态流转 待处理→已确认→已处理→已关闭。

### UI（报警中心）

- 新导航项"报警中心"（权限点 `alert:view`，`Station:OpAuth` 默认需登录）；
- `AlertModuleView`：级别/状态筛选、报警列表（级别徽标/标题/详情/来源/时间/状态）、选中报警后 确认/处理/关闭（仅 `alert:handle` 权限可见，admin/部门负责人可操作，操作员仅查看）；
- **新报警横幅**：壳窗口每 5 秒轮询待处理数，>0 显示红色横幅"⚠ 有 N 条待处理报警（点击查看）"，点击直达报警中心；
- 工作台与采集页产生的报警（非授权接入/绑定异常等）自动进入列表并触发横幅。

## 2. 验证结果（SQLite + Kingbase × 4 项）

| 验证项 | SQLite | Kingbase |
|---|---|---|
| 权限映射（admin 可处理、操作员仅查看） | ✅ | ✅ |
| 写入 + 级别/状态筛选 + 待处理计数 | ✅ | ✅ |
| 状态流转（确认→处理→关闭，计数正确） | ✅ | ✅ |
| 清理 | ✅ | ✅ |

验证代码：[spikes/Station.Spike.Alerts](../../spikes/Station.Spike.Alerts/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.Alerts -c Release -- --db sqlite
dotnet run --project spikes/Station.Spike.Alerts -c Release -- --db kingbase
```

## 3. 说明

1. 报警数据模型沿用 M9 的 `Alert`（类型/级别/状态/来源/部门）；
2. 声音提示、弹窗强提醒、邮件短信通知为 P1，界面横幅为 P0 即时提示；
3. 后续报警处置流程（指派/复核/超时升级）属 P2 复杂合规流程，未在本里程碑实现。

## 4. 后续（M11 建议）

- 平台元数据上报（M1 契约落地：注册/配置同步/元数据/报警/远程指令）；
- 真实 UMS/MTP 采集源接入与断点续传真实验证；
- 报警处置增强（指派/复核）与日志中心三页签（操作/设备/报警）。
