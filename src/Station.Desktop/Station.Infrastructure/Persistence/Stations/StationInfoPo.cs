using SqlSugar;

namespace Station.Infrastructure.Persistence.Stations;

[SugarTable("station_info")]
public sealed class StationInfoPo
{
    [SugarColumn(IsPrimaryKey = true, Length = 36)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [SugarColumn(Length = 64)] public string StationUid { get; set; } = "";
    [SugarColumn(Length = 64)] public string StationCode { get; set; } = "";
    [SugarColumn(Length = 128)] public string StationName { get; set; } = "";
    [SugarColumn(Length = 128)] public string ProductSerialNumber { get; set; } = "";
    [SugarColumn(Length = 128)] public string Model { get; set; } = "";
    [SugarColumn(Length = 256)] public string OperatingSystem { get; set; } = "";
    public int UsbPortCount { get; set; }
    [SugarColumn(Length = 256)] public string Location { get; set; } = "";
    [SugarColumn(Length = 128)] public string Department { get; set; } = "";
    [SugarColumn(Length = 16)] public string ManagementMode { get; set; } = "Standalone";
    [SugarColumn(Length = 16)] public long PlatformVersion { get; set; }
    public DateTime UpdatedAt { get; set; }
}
