namespace Station.Domain.Stations;

/// <summary>采集站元信息（单行表，POCO，无聚合行为）。</summary>
public sealed class StationInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StationUid { get; set; } = string.Empty;
    public string StationCode { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public string ProductSerialNumber { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public int UsbPortCount { get; set; }
    public string Location { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string ManagementMode { get; set; } = "Standalone";
    public long PlatformVersion { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
