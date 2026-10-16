using Station.Domain.Settings;

namespace Station.Infrastructure.Persistence.Settings;

internal static class SysSettingMapper
{
    public static SysSetting ToDomain(SysSettingPo po) => new()
    {
        Id = po.Id,
        GroupKey = po.GroupKey,
        SubKey = po.SubKey,
        ValueJson = po.ValueJson,
        ValueType = po.ValueType,
        IsEncrypted = po.IsEncrypted,
        Version = po.Version,
        UpdatedAt = po.UpdatedAt,
        UpdatedBy = po.UpdatedBy,
        Remark = po.Remark
    };

    public static SysSettingPo ToPo(SysSetting s) => new()
    {
        Id = s.Id,
        GroupKey = s.GroupKey,
        SubKey = s.SubKey,
        ValueJson = s.ValueJson,
        ValueType = s.ValueType,
        IsEncrypted = s.IsEncrypted,
        Version = s.Version,
        UpdatedAt = s.UpdatedAt,
        UpdatedBy = s.UpdatedBy,
        Remark = s.Remark
    };
}
