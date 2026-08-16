# M53：工作台紧急优先 + 卡片布局可配置

> 状态：✅ 通过（2026-08-16，Station.Spike.DesktopAudit 17/17 + 双端全量构建 + linux-x64 发布）
> 目标：紧急优先改为操作员在卡片上直接操作（非设备上报），上限可配置；30 路固定通道改为设置模块可配置的行数/每行卡片数/卡片宽高动态排布。

## 1. 紧急优先（操作员卡片操作 + 配额控制）

- `CollectTask.IsEmergency`（存量库 `EnsureColumn("station_collect_task","IsEmergency","int")` 自动补列，新库 CodeFirst 建列）；
- `ICollectTaskService.SetEmergencyAsync(taskId, isEmergency)`：标记/取消紧急优先，服务层按"活跃任务中紧急数 ≥ 上限"强制拦截（上限 `CollectOptions.MaxEmergencyTasks`，默认 3）；
- 工作台卡片新增"优先/取消优先"按钮（采集中/已暂停任务可操作，需登录 + collect 权限），超限时提示"已达上限，请先取消其他优先任务"；
- 紧急卡片样式：红色顶条 + 浅红底色（对齐原型 emergency-card），取消后恢复。

## 2. 卡片布局动态化（设置模块配置）

- 新增 `WorkbenchOptions`（Station:Workbench）：行数（默认 6）/ 每行卡片数（默认 5）/ 卡片宽（240px）/ 卡片高（200px）；总通道 = 行数 × 每行卡片数；
- 系统设置新增"工作台显示"分组（Web 单机后台 + 桌面端设置模块均可见可改）：行数、每行卡片数、卡片宽高、紧急优先上限；`SettingsController` 补 workbench 分组读写；
- 工作台每 2 秒检查布局配置，变化即重建卡片网格（保留端口→任务映射）；卡片 Width/Height/列数绑定可观察属性，动态生效无需重启；
- 运行时文件持久化（Station:Workbench + Collect.MaxEmergencyTasks），重启后保持。

## 3. 验证（Station.Spike.DesktopAudit 17/17）

| 场景 | 结果 |
|---|---|
| 设置含工作台分组（默认 6×5/240×200/上限 3） | ✅ |
| 工作台设置修改生效（4×6/260×210/上限 2）并落运行时文件 | ✅ |
| 紧急上限强制：第 3 个任务标记被拒；取消一个后可再标记 | ✅ |
| 任务 DTO 含紧急标记；活跃任务查询正确 | ✅ |
| 桌面/平台全量构建 + linux-x64 单文件发布 | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.DesktopAudit -c Release
```
