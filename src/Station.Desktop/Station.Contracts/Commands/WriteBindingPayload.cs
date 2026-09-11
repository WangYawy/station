namespace Station.Contracts.Commands;

/// <summary>写入记录仪绑定信息指令载荷：平台台账重新绑定 → 采集站更新本地台账（接入时自动写 ini）。</summary>
public sealed record WriteBindingPayload(
    string RecorderSerial,
    string? Model,
    int? Protocol,
    string? UserNo,
    string? UserName,
    string? DeptCode,
    string? DeptName,
    long? DeptId,
    DateTime? BoundAt);
