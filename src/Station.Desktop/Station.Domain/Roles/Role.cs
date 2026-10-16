namespace Station.Domain.Roles;

/// <summary>角色。IsSystem=true 时编码不可改、不可删除。</summary>
public sealed class Role
{
    public long Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public DataScope DataScope { get; private set; }
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public long? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public long? UpdatedBy { get; private set; }

    private Role() { }

    public static Role Create(
        long id, string code, string name, DataScope dataScope, bool isSystem,
        DateTime now, long? createdBy = null)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("角色编码不能为空。", nameof(code));
        if (code.Length > 32) throw new ArgumentException("角色编码过长。", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("角色名称不能为空。", nameof(name));

        return new Role
        {
            Id = id,
            Code = code,
            Name = name,
            DataScope = dataScope,
            IsSystem = isSystem,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = createdBy
        };
    }

    public static Role Restore(
        long id, string code, string name, DataScope dataScope, bool isSystem, bool isActive,
        DateTime createdAt, long? createdBy, DateTime? updatedAt, long? updatedBy)
        => new()
        {
            Id = id,
            Code = code,
            Name = name,
            DataScope = dataScope,
            IsSystem = isSystem,
            IsActive = isActive,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            UpdatedAt = updatedAt,
            UpdatedBy = updatedBy
        };

    public void Rename(string name, DateTime now, long? updatedBy = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("角色名称不能为空。", nameof(name));
        Name = name; Touch(now, updatedBy);
    }

    public void ChangeCode(string code, DateTime now, long? updatedBy = null)
    {
        if (IsSystem) throw new InvalidOperationException("预置角色编码不可修改。");
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("角色编码不能为空。", nameof(code));
        Code = code; Touch(now, updatedBy);
    }

    public void ChangeDataScope(DataScope dataScope, DateTime now, long? updatedBy = null)
    {
        DataScope = dataScope; Touch(now, updatedBy);
    }

    public void Activate(DateTime now, long? updatedBy = null) { IsActive = true; Touch(now, updatedBy); }
    public void Deactivate(DateTime now, long? updatedBy = null) { IsActive = false; Touch(now, updatedBy); }

    private void Touch(DateTime now, long? updatedBy) { UpdatedAt = now; UpdatedBy = updatedBy; }
}
