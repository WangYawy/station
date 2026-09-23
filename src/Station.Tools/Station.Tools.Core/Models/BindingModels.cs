namespace Station.Tools.Core.Models;

/// <summary>记录仪绑定信息。</summary>
public sealed record BindingInfo(
    string RecorderSerial,
    string RecorderModel,
    string UserNo,
    string UserName,
    string DeptCode,
    string DeptName,
    DateTime BoundAt,
    string Mac);
