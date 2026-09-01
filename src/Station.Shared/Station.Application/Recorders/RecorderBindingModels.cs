namespace Station.Application.Recorders;

/// <summary>记录仪根目录绑定文件（station_bind.ini）解析结果。</summary>
public sealed record BindingInfo(
    string RecorderSerial,
    string RecorderModel,
    string UserNo,
    string UserName,
    string DeptCode,
    string DeptName,
    DateTime BoundAt,
    string Signature);
