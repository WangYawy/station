namespace Station.Domain.Users;

/// <summary>用户（人员）。工号为跨端自然键。</summary>
public sealed class User
{
    public long Id { get; private set; }
    public string UserNo { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public long DeptId { get; private set; }
    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public long? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public long? UpdatedBy { get; private set; }

    private User() { }

    public static User Create(
        long id, string userNo, string name, long deptId,
        DateTime now, long? createdBy = null)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (string.IsNullOrWhiteSpace(userNo)) throw new ArgumentException("工号不能为空。", nameof(userNo));
        if (userNo.Length > 32) throw new ArgumentException("工号长度不能超过 32。", nameof(userNo));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("姓名不能为空。", nameof(name));
        if (deptId <= 0) throw new ArgumentOutOfRangeException(nameof(deptId));

        return new User
        {
            Id = id,
            UserNo = userNo,
            Name = name,
            DeptId = deptId,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = createdBy
        };
    }

    public static User Restore(
        long id, string userNo, string name, long deptId, bool isActive,
        DateTime createdAt, long? createdBy, DateTime? updatedAt, long? updatedBy)
        => new()
        {
            Id = id,
            UserNo = userNo,
            Name = name,
            DeptId = deptId,
            IsActive = isActive,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            UpdatedAt = updatedAt,
            UpdatedBy = updatedBy
        };

    public void UpdateBasicInfo(string userNo, string name, DateTime now, long? updatedBy = null)
    {
        if (string.IsNullOrWhiteSpace(userNo)) throw new ArgumentException("工号不能为空。", nameof(userNo));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("姓名不能为空。", nameof(name));
        UserNo = userNo; Name = name; Touch(now, updatedBy);
    }

    public void ChangeDept(long deptId, DateTime now, long? updatedBy = null)
    {
        if (deptId <= 0) throw new ArgumentOutOfRangeException(nameof(deptId));
        DeptId = deptId; Touch(now, updatedBy);
    }

    public void Activate(DateTime now, long? updatedBy = null) { IsActive = true; Touch(now, updatedBy); }
    public void Deactivate(DateTime now, long? updatedBy = null) { IsActive = false; Touch(now, updatedBy); }

    private void Touch(DateTime now, long? updatedBy) { UpdatedAt = now; UpdatedBy = updatedBy; }
}
