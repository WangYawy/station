using Station.Domain.Enums;

namespace Station.Application.Roles.Commands;

/// <summary>创建角色命令。IsSystem 由系统内部决定，不暴露给外部输入。</summary>
public sealed class CreateRoleCommand
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DataScope DataScope { get; init; } = DataScope.Self;
    public bool IsActive { get; init; } = true;
}
