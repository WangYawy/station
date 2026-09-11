namespace Station.Contracts.Binding;

/// <summary>
/// 记录仪绑定信息（写入记录仪根目录 ini 的载荷）。
/// 记录仪编号、用户工号、部门编码均为跨端业务编号（自然键）。
/// </summary>
public sealed record RecorderBindingInfo
{
    public required string RecorderSerial { get; init; }

    public required string UserNo { get; init; }

    public required string DeptCode { get; init; }

    public DateTime BoundAt { get; init; }

    /// <summary>对上述字段拼接值计算的 SM3 校验值，防止篡改。</summary>
    public required string Sm3 { get; init; }
}
