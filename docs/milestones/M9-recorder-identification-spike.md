# M9：记录仪接入识别与归属（ini 绑定 + 三层识别 + 报警 + 采集归属继承）

> 状态：✅ 通过（2026-08-13，SQLite + Kingbase 6/6）
> 依据需求：记录仪根目录 ini 绑定（编号/用户/部门/时间 + SM3 校验）；接入三层识别（无 ini=未绑定、ini 校验失败=疑似篡改、编号不在台账=非授权接入）；已授权设备归属自动继承；不一致按台账自动重写 ini。

## 1. 交付内容

### 领域层

| 实体 | 表名 | 说明 |
|---|---|---|
| `Recorder` | `station_recorder` | 台账/白名单（序列号唯一、绑定用户/部门、授权标记） |
| `Alert` | `station_alert` | 报警（类型/级别/状态/来源，P0 界面列表） |

### 绑定文件（Station.Infrastructure.Recorders）

- `RecorderBindingFile`：读写记录仪根目录 `station_bind.ini`：
  `[binding] recorder_serial / recorder_model / user_no / user_name / dept_code / dept_name / bound_at / sm3`
- 签名 = SM3(`serial|model|userNo|userName|deptCode|deptName|boundAt|secret`)，密钥配置 `Station:Binding:Secret`（防篡改）。

### 应用层（Station.Application.Recorders）

- `IRecorderService`：台账注册/更新/查询、**写入绑定**（更新台账 + 写 ini）、解除绑定（清台账 + 删 ini）；
- `IRecorderIdentificationService.IdentifyAsync`：**三层识别**：
  1. 无 ini → `NoBinding`（未绑定）→ 拒绝 + BindingInvalid 报警；
  2. ini 签名校验失败 → `InvalidSignature`（疑似篡改）→ 拒绝 + Critical 报警；
  3. 编号不在台账/未授权 → `UnknownRecorder`（非授权接入）→ 拒绝 + UnauthorizedAccess 报警；
  4. 通过 → `Bound`（返回用户/部门归属）；**ini 与台账不一致时按台账自动重写 ini**。
- `IAlertService`：报警写入。
- 采集归属继承：`CollectDeviceInfo` 携带 UserId/DeptId，任务创建时写入 `OperatorUserId/DeptId`；`ICollectSource.GetRecorderRoot` 提供绑定文件读写根目录（模拟源=模拟目录，UMS=盘符根，MTP=P1）。

### UI（采集作业页）

- "注册并绑定（模拟）"：注册演示记录仪到台账并写入当前登录用户/部门绑定；
- "模拟接入"：先走三层识别 → 已绑定才自动采集（提示归属）；未绑定/篡改/非授权 → 拒绝并提示；
- "解除绑定"：删 ini + 清台账绑定。

## 2. 验证结果（SQLite + Kingbase × 6 项）

| 验证项 | SQLite | Kingbase |
|---|---|---|
| 注册 + 写绑定（ini 存在、SM3 签名有效、台账白名单） | ✅ | ✅ |
| 识别已绑定 → 归属返回 + 采集任务操作人/部门继承 | ✅ | ✅ |
| 篡改 ini → InvalidSignature + BindingInvalid 报警 | ✅ | ✅ |
| 删除 ini → NoBinding + 报警 | ✅ | ✅ |
| 非授权编号（不在台账）→ UnknownRecorder + UnauthorizedAccess 报警 | ✅ | ✅ |
| 清理 | ✅ | ✅ |

验证代码：[spikes/Station.Spike.Recorder](../../spikes/Station.Spike.Recorder/Program.cs)

```powershell
dotnet run --project spikes/Station.Spike.Recorder -c Release -- --db sqlite
dotnet run --project spikes/Station.Spike.Recorder -c Release -- --db kingbase
```

## 3. 设计说明

1. **台账是绑定权威，ini 是载体**：识别时若 ini 与台账绑定不一致 → 按台账自动重写 ini（需求 10.1 第 3 层）。
2. **归属继承**：识别结果（UserId/DeptId）随设备信息进入采集任务，文件台账后续按任务归属落库。
3. **报警最小闭环**：三层识别失败均写 `Alert`（BindingInvalid / UnauthorizedAccess），界面列表展示、声音/弹窗/邮件短信按 P1 后续接入。
4. **模拟记录仪**：模拟源目录即"记录仪根目录"，绑定文件直接落盘，端到端可验证；真实 UMS 源（盘符根）后续替换 `GetRecorderRoot`/`ScanAsync` 即可，业务层不变。

## 4. 配置

```json
{
  "Station": {
    "Binding": { "Secret": "station-dev-binding-secret" }
  }
}
```

## 5. 后续（M10 建议）

- 真实 UMS/MTP/私有加密采集源接入（盘符枚举、WPD、SDK 转 U 盘）；
- 报警中心模块（列表/确认/处理/声音弹窗）；
- 平台元数据上报（M1 契约：注册/配置同步/元数据/报警/远程指令）；
- 记录仪台账管理页（Web/桌面）与"写入绑定"操作入口。
