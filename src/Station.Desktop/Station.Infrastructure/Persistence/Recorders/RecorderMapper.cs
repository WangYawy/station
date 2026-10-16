using Station.Domain.Recorders;

namespace Station.Infrastructure.Persistence.Recorders;

internal static class RecorderMapper
{
    public static Recorder ToDomain(RecorderPo po)
        => Recorder.Restore(
            po.Id, po.SerialNumber, po.Code, po.Name, po.Model, po.Protocol,
            po.BoundUserId, po.DeptId, po.IsAuthorized, po.IsActive,
            po.CreatedAt, po.CreatedBy, po.UpdatedAt, po.UpdatedBy);

    public static RecorderPo ToPo(Recorder r)
        => new()
        {
            Id = r.Id,
            SerialNumber = r.SerialNumber,
            Code = r.Code,
            Name = r.Name,
            Model = r.Model,
            Protocol = r.Protocol,
            BoundUserId = r.BoundUserId,
            DeptId = r.DeptId,
            IsAuthorized = r.IsAuthorized,
            IsActive = r.IsActive,
            CreatedAt = r.CreatedAt,
            CreatedBy = r.CreatedBy,
            UpdatedAt = r.UpdatedAt,
            UpdatedBy = r.UpdatedBy
        };
}
