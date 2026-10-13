namespace Station.Domain.Users;

/// <summary>用户-角色关联。</summary>
public sealed class UserRole
{
    public long Id { get; private set; }
    public long UserId { get; private set; }
    public long RoleId { get; private set; }

    private UserRole() { }

    public static UserRole Create(long id, long userId, long roleId)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        if (roleId <= 0) throw new ArgumentOutOfRangeException(nameof(roleId));
        return new UserRole { Id = id, UserId = userId, RoleId = roleId };
    }
}
