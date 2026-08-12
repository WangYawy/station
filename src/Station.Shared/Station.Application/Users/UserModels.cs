using Station.Domain.Enums;

namespace Station.Application.Users;

public sealed record DeptDto(long? Id, string Code, string Name, long? ParentId, int SortOrder, bool IsActive = true);

public sealed record UserDto(long? Id, string UserNo, string Name, long DeptId, bool IsActive = true);

public sealed record RoleDto(long? Id, string Code, string Name, DataScope DataScope, bool IsSystem = false, bool IsActive = true);

public sealed record PermissionDto(long Id, string Code, string Name, string Module);

public sealed record CreateAccountResult(bool Success, string? Message, long? AccountId = null);

public sealed record OpResult(bool Success, string? Message = null);
