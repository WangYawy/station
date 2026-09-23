using Station.Contracts;
using Station.Domain.Collecting;
using Station.Domain.Entities;

namespace Station.Application.Recorders;

public sealed record RecorderDto(
    long? Id,
    string SerialNumber,
    string Model,
    ProtocolType Protocol,
    long? BoundUserId,
    long? DeptId,
    bool IsAuthorized,
    bool IsActive);

/// <summary>
/// 记录仪识别结果。
/// 
/// 【判定优先级】
///   Matched > Mismatch > UserInactive/DeptInactive/RecorderMissing > Tampered/Legacy > NotFound
/// 只有 Matched 才允许进入采集流程。
/// </summary>
public sealed record RecorderIdentifyResult(
    RecorderIdentifyStatus Status,
    CollectDeviceInfo Device,
    BindingInfo? Binding,
    Recorder? Recorder,
    User? User,
    Dept? Dept,
    string? Message)
{
    /// <summary>是否可以通过识别进入采集。</summary>
    public bool CanCollect => Status == RecorderIdentifyStatus.Matched;

    /// <summary>是否需要用户重新绑定。</summary>
    public bool NeedsRebind =>
        Status is RecorderIdentifyStatus.NotFound
            or RecorderIdentifyStatus.Legacy
            or RecorderIdentifyStatus.Tampered
            or RecorderIdentifyStatus.RecorderMissing
            or RecorderIdentifyStatus.Mismatch;

    // ---- 工厂方法 ----

    public static RecorderIdentifyResult NotFound(CollectDeviceInfo d, string msg) =>
        new(RecorderIdentifyStatus.NotFound, d, null, null, null, null, msg);

    public static RecorderIdentifyResult Legacy(CollectDeviceInfo d, BindingInfo b, string msg) =>
        new(RecorderIdentifyStatus.Legacy, d, b, null, null, null, msg);

    public static RecorderIdentifyResult Tampered(CollectDeviceInfo d, BindingInfo b, string msg) =>
        new(RecorderIdentifyStatus.Tampered, d, b, null, null, null, msg);

    public static RecorderIdentifyResult RecorderMissing(CollectDeviceInfo d, BindingInfo b, string msg) =>
        new(RecorderIdentifyStatus.RecorderMissing, d, b, null, null, null, msg);

    public static RecorderIdentifyResult UserInactive(CollectDeviceInfo d, BindingInfo b, string msg) =>
        new(RecorderIdentifyStatus.UserInactive, d, b, null, null, null, msg);

    public static RecorderIdentifyResult DeptInactive(CollectDeviceInfo d, BindingInfo b, string msg) =>
        new(RecorderIdentifyStatus.DeptInactive, d, b, null, null, null, msg);

    public static RecorderIdentifyResult Mismatch(CollectDeviceInfo d, BindingInfo b, string msg) =>
        new(RecorderIdentifyStatus.Mismatch, d, b, null, null, null, msg);

    public static RecorderIdentifyResult Matched(
        CollectDeviceInfo d, BindingInfo b, Recorder r, User? u, Dept? dept) =>
        new(RecorderIdentifyStatus.Matched, d, b, r, u, dept, null);
}

/// <summary>识别状态。</summary>
public enum RecorderIdentifyStatus
{
    /// <summary>全部校验通过，可采集。</summary>
    Matched = 0,

    /// <summary>无绑定文件。</summary>
    NotFound = 1,

    /// <summary>绑定文件是旧格式（无 mac 字段），需重新绑定。</summary>
    Legacy = 2,

    /// <summary>绑定文件 MAC 校验失败（被篡改或密钥不匹配）。</summary>
    Tampered = 3,

    /// <summary>记录仪不在台账或已停用。</summary>
    RecorderMissing = 4,

    /// <summary>绑定用户不在册或已禁用。</summary>
    UserInactive = 5,

    /// <summary>绑定部门不在册或已禁用。</summary>
    DeptInactive = 6,

    /// <summary>台账绑定与文件绑定不一致。</summary>
    Mismatch = 7
}
