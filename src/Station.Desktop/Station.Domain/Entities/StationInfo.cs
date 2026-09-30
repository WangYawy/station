using SqlSugar;

namespace Station.Domain.Entities;

[SugarTable("station_info")]
public class StationInfo
{
    [SugarColumn(IsPrimaryKey = true, Length = 36)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>采集站唯一标识（平台版由平台分配，单机版本地生成）</summary>
    [SugarColumn(Length = 64)]
    public string StationUid { get; set; } = string.Empty;

    /// <summary>采集站编号（上传目录命名等业务场景使用）</summary>
    [SugarColumn(Length = 64)]
    public string StationCode { get; set; } = string.Empty;

    /// <summary>采集站名称</summary>
    [SugarColumn(Length = 128)]
    public string StationName { get; set; } = string.Empty;

    /// <summary>产品序列号（首次可预填硬件读取值，管理员可覆盖）</summary>
    [SugarColumn(Length = 128)]
    public string ProductSerialNumber { get; set; } = string.Empty;

    /// <summary>型号（首次可预填硬件读取值，管理员可覆盖）</summary>
    [SugarColumn(Length = 128)]
    public string Model { get; set; } = string.Empty;

    /// <summary>操作系统描述（首次初始化时自动采集一次）</summary>
    [SugarColumn(Length = 256)]
    public string OperatingSystem { get; set; } = string.Empty;

    /// <summary>USB 端口数量（管理员按采集站型号规格初始化）</summary>
    public int UsbPortCount { get; set; }

    /// <summary>所在位置</summary>
    [SugarColumn(Length = 256)]
    public string Location { get; set; } = string.Empty;

    /// <summary>所属部门</summary>
    [SugarColumn(Length = 128)]
    public string Department { get; set; } = string.Empty;

    /// <summary>管理模式：Standalone / Platform</summary>
    [SugarColumn(Length = 16)]
    public string ManagementMode { get; set; } = "Standalone";

    /// <summary>平台下发的版本号（单机版为 0）</summary>
    public long PlatformVersion { get; set; }

    /// <summary>最后更新时间</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
