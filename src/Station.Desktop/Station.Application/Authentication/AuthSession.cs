using Station.Domain.Roles;

namespace Station.Application.Authentication;

/// <summary>当前登录会话：账号 + 人员 + 角色 + 权限 + 数据范围。</summary>
public sealed record AuthSession(
    long AccountId,
    long? UserId,
    string UserName,
    string? UserNo,
    string? Name,
    long? DeptId,
    DataScope DataScope,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);
