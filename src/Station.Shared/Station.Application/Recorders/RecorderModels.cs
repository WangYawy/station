using Station.Contracts;

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

/// <summary>接入识别结果：三层识别（无绑定/疑似篡改/非授权）→ 已绑定。</summary>
public enum RecorderIdentifyStatus
{
    Bound,
    NoBinding,
    InvalidSignature,
    UnknownRecorder
}

public sealed record RecorderIdentifyResult(
    RecorderIdentifyStatus Status,
    long? UserId,
    long? DeptId,
    string Message);
