using Station.Domain.Users;

namespace Station.Infrastructure.Persistence.Users;

internal static class UserMapper
{
    public static User ToDomain(UserPo po)
        => User.Restore(
            po.Id, po.UserNo, po.Name, po.DeptId, po.IsActive,
            po.CreatedAt, po.CreatedBy, po.UpdatedAt, po.UpdatedBy);

    public static UserPo ToPo(User u)
        => new()
        {
            Id = u.Id,
            UserNo = u.UserNo,
            Name = u.Name,
            DeptId = u.DeptId,
            IsActive = u.IsActive,
            CreatedAt = u.CreatedAt,
            CreatedBy = u.CreatedBy,
            UpdatedAt = u.UpdatedAt,
            UpdatedBy = u.UpdatedBy
        };
}
