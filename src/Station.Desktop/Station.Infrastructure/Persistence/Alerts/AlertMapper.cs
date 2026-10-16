using Station.Domain.Alerts;

namespace Station.Infrastructure.Persistence.Alerts;

internal static class AlertMapper
{
    public static Alert ToDomain(AlertPo po)
        => Alert.Restore(
            po.Id, po.Type, po.Level, po.Status,
            po.Title, po.Detail, po.Source, po.DeptId,
            po.CreatedAt, po.UpdatedAt, po.UpdatedBy);

    public static AlertPo ToPo(Alert a)
        => new()
        {
            Id = a.Id,
            Type = a.Type,
            Level = a.Level,
            Status = a.Status,
            Title = a.Title,
            Detail = a.Detail,
            Source = a.Source,
            DeptId = a.DeptId,
            CreatedAt = a.CreatedAt,
            UpdatedAt = a.UpdatedAt,
            UpdatedBy = a.UpdatedBy
        };
}
