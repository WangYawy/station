namespace Station.Domain.Permissions;

/// <summary>权限点。Code 为跨版本稳定标识，创建后不可变。</summary>
public sealed class Permission
{
    public long Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Module { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public long? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public long? UpdatedBy { get; private set; }

    private Permission() { }

    public static Permission Create(
        long id, string code, string name, string module,
        DateTime now, long? createdBy = null)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("权限编码不能为空。", nameof(code));
        if (code.Length > 64) throw new ArgumentException("权限编码过长。", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("权限名称不能为空。", nameof(name));
        if (string.IsNullOrWhiteSpace(module)) throw new ArgumentException("权限模块不能为空。", nameof(module));

        return new Permission
        {
            Id = id,
            Code = code,
            Name = name,
            Module = module,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = createdBy
        };
    }

    public static Permission Restore(
        long id, string code, string name, string module, bool isActive,
        DateTime createdAt, long? createdBy, DateTime? updatedAt, long? updatedBy)
        => new()
        {
            Id = id,
            Code = code,
            Name = name,
            Module = module,
            IsActive = isActive,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            UpdatedAt = updatedAt,
            UpdatedBy = updatedBy
        };

    public void Rename(string name, DateTime now, long? updatedBy = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("权限名称不能为空。", nameof(name));
        Name = name;
        Touch(now, updatedBy);
    }

    public void Activate(DateTime now, long? updatedBy = null) { IsActive = true; Touch(now, updatedBy); }
    public void Deactivate(DateTime now, long? updatedBy = null) { IsActive = false; Touch(now, updatedBy); }

    private void Touch(DateTime now, long? updatedBy)
    {
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }
}
