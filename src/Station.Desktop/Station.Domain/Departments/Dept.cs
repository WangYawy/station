namespace Station.Domain.Departments;

/// <summary>部门（组织节点）。Code 为跨端自然键，创建后不可变。</summary>
public sealed class Dept
{
    public long Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public long? ParentId { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public long? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public long? UpdatedBy { get; private set; }

    private Dept() { }

    public static Dept Create(
        long id, string code, string name, long? parentId, int sortOrder,
        DateTime now, long? createdBy = null)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("部门编码不能为空。", nameof(code));
        if (code.Length > 32) throw new ArgumentException("部门编码长度不能超过 32。", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("部门名称不能为空。", nameof(name));
        if (name.Length > 64) throw new ArgumentException("部门名称长度不能超过 64。", nameof(name));

        return new Dept
        {
            Id = id,
            Code = code,
            Name = name,
            ParentId = parentId,
            SortOrder = sortOrder,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = createdBy
        };
    }

    public static Dept Restore(
        long id, string code, string name, long? parentId, int sortOrder, bool isActive,
        DateTime createdAt, long? createdBy, DateTime? updatedAt, long? updatedBy)
        => new()
        {
            Id = id,
            Code = code,
            Name = name,
            ParentId = parentId,
            SortOrder = sortOrder,
            IsActive = isActive,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            UpdatedAt = updatedAt,
            UpdatedBy = updatedBy
        };

    public void UpdateInfo(string name, int sortOrder, DateTime now, long? updatedBy = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("部门名称不能为空。", nameof(name));
        Name = name;
        SortOrder = sortOrder;
        Touch(now, updatedBy);
    }

    public void MoveTo(long? parentId, DateTime now, long? updatedBy = null)
    {
        if (parentId == Id) throw new InvalidOperationException("部门不能作为自己的上级。");
        ParentId = parentId;
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
