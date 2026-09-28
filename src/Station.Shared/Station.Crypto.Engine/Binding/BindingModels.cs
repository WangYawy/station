namespace Station.Crypto.Engine.Models;

/// <summary>记录仪绑定信息。</summary>
public sealed record BindingInfo(
    string DeviceSerial,
    string DeviceModel,
    string UserNo,
    string UserName,
    string DeptCode,
    string DeptName,
    DateTime BoundAt,
    string Mac);
