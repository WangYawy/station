# M45：文件归属修正（留痕 + SM2 签名）

> 状态：✅ 通过（2026-08-13，Spike 3/3）
> 目标：P1 增强——管理员可修正文件归属（用户/部门），修正永久留痕且平台私钥 SM2 签名，防抵赖。

## 1. 交付内容

- `PlatformFileCorrection`（platform_file_correction）：文件归属修正留痕表（FileNo、修正前后用户/部门、操作人、时间、SM2 签名）；
- `PUT /api/v1/files/{fileNo}/ownership`（`file:manage` + 数据范围）：更新文件归属，写入签名留痕（canonical = FileNo|旧用户|新用户|旧部门|新部门|操作人|修正时间 UTC，平台指令私钥签名，未配密钥为 `unsigned`），并落审计 `file.correct`；
- `GET /api/v1/files/{fileNo}/corrections`（`file:view` + 数据范围）：查询修正记录；
- 前端：文件检索表格新增"归属"列"修正"按钮（`file:manage`）→ 弹窗输入用户/部门 → 确认修正并提示已签名留痕。

## 2. 验证结果（Spike 3/3）

| 场景 | 结果 |
|---|---|
| 归属修正：PUT 后文件用户/部门更新（zhangsan/GRP1） | ✅ |
| 修正留痕：记录含非 `unsigned` 签名，用平台公钥重算 canonical 验签通过 | ✅ |
| 操作员修正范围外文件 → 404（有 file:manage 但文件不在其数据范围） | ✅ |

```powershell
dotnet run --project spikes/Station.Spike.PlatformFileCorrection -c Release
```

## 3. 说明与后续

- 修正范围受数据权限约束：操作员仅可修正本范围文件，管理员全量；
- 继续 P1：白名单等全量配置下发、xlsx/PDF 导出、SignalR 实时推送。
