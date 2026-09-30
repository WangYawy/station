namespace Station.Crypto;

/// <summary>
/// 记录仪绑定信息。
///
/// 序列化到 station_bind.ini 的字段名由
/// <see cref="BindingFileCodec"/> 统一管理。
/// </summary>
public sealed record BindingInfo(
    string DeviceSerial,
    string DeviceModel,
    string UserNo,
    string UserName,
    string DeptCode,
    string DeptName,
    DateTime BoundAt,
    string Signature);
