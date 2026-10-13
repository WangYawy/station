using Station.Domain.Departments;

namespace Station.Infrastructure.Persistence.Departments;

internal static class DeptMapper
{
    public static Dept ToDomain(DeptPo po)
        => Dept.Restore(
            po.Id, po.Code, po.Name, po.ParentId, po.SortOrder, po.IsActive,
            po.CreatedAt, po.CreatedBy, po.UpdatedAt, po.UpdatedBy);

    public static DeptPo ToPo(Dept d)
        => new()
        {
            Id = d.Id,
            Code = d.Code,
            Name = d.Name,
            ParentId = d.ParentId,
            SortOrder = d.SortOrder,
            IsActive = d.IsActive,
            CreatedAt = d.CreatedAt,
            CreatedBy = d.CreatedBy,
            UpdatedAt = d.UpdatedAt,
            UpdatedBy = d.UpdatedBy
        };
}
