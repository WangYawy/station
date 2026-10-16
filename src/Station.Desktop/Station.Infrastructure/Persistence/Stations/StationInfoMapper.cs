using Station.Domain.Stations;

namespace Station.Infrastructure.Persistence.Stations;

internal static class StationInfoMapper
{
    public static StationInfo ToDomain(StationInfoPo po) => new()
    {
        Id = po.Id,
        StationUid = po.StationUid,
        StationCode = po.StationCode,
        StationName = po.StationName,
        ProductSerialNumber = po.ProductSerialNumber,
        Model = po.Model,
        OperatingSystem = po.OperatingSystem,
        UsbPortCount = po.UsbPortCount,
        Location = po.Location,
        Department = po.Department,
        ManagementMode = po.ManagementMode,
        PlatformVersion = po.PlatformVersion,
        UpdatedAt = po.UpdatedAt
    };

    public static StationInfoPo ToPo(StationInfo s) => new()
    {
        Id = s.Id,
        StationUid = s.StationUid,
        StationCode = s.StationCode,
        StationName = s.StationName,
        ProductSerialNumber = s.ProductSerialNumber,
        Model = s.Model,
        OperatingSystem = s.OperatingSystem,
        UsbPortCount = s.UsbPortCount,
        Location = s.Location,
        Department = s.Department,
        ManagementMode = s.ManagementMode,
        PlatformVersion = s.PlatformVersion,
        UpdatedAt = s.UpdatedAt
    };
}
