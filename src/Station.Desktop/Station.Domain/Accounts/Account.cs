namespace Station.Domain.Accounts;

/// <summary>
/// 账号聚合根：登录主体与 RBAC 主体。
/// 用户名创建后不可变；密码哈希由外部安全服务生成后传入。
/// </summary>
public sealed class Account
{
    public long Id { get; private set; }
    public string UserName { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public long? UserId { get; private set; }
    public bool IsEnabled { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTime? LockedUntil { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public long? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public long? UpdatedBy { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public long? DeletedBy { get; private set; }

    private Account() { }

    public static Account Create(
        long id,
        string userName,
        string passwordHash,
        long? userId,
        DateTime now,
        long? createdBy = null)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id), "Id 必须由 IIdGenerator 预先分配。");
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("用户名不能为空。", nameof(userName));
        if (userName.Length > 64)
            throw new ArgumentException("用户名长度不能超过 64。", nameof(userName));
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("密码哈希不能为空。", nameof(passwordHash));

        return new Account
        {
            Id = id,
            UserName = userName,
            PasswordHash = passwordHash,
            UserId = userId,
            IsEnabled = true,
            FailedLoginAttempts = 0,
            LockedUntil = null,
            CreatedAt = now,
            CreatedBy = createdBy,
            IsDeleted = false
        };
    }

    /// <summary>由持久化层重建聚合（Infrastructure 专用）。</summary>
    public static Account Restore(
        long id,
        string userName,
        string passwordHash,
        long? userId,
        bool isEnabled,
        int failedLoginAttempts,
        DateTime? lockedUntil,
        DateTime createdAt,
        long? createdBy,
        DateTime? updatedAt,
        long? updatedBy,
        bool isDeleted,
        DateTime? deletedAt,
        long? deletedBy)
        => new()
        {
            Id = id,
            UserName = userName,
            PasswordHash = passwordHash,
            UserId = userId,
            IsEnabled = isEnabled,
            FailedLoginAttempts = failedLoginAttempts,
            LockedUntil = lockedUntil,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            UpdatedAt = updatedAt,
            UpdatedBy = updatedBy,
            IsDeleted = isDeleted,
            DeletedAt = deletedAt,
            DeletedBy = deletedBy
        };

    public bool IsLocked(DateTime now) => LockedUntil is { } until && until > now;

    public LoginFailureOutcome RecordLoginFailure(
        DateTime now,
        int maxFailedAttempts,
        TimeSpan lockDuration,
        long? updatedBy = null)
    {
        if (maxFailedAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFailedAttempts));
        if (lockDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lockDuration));

        if (IsLocked(now))
            return LoginFailureOutcome.LockedOut;

        FailedLoginAttempts++;
        UpdatedAt = now;
        UpdatedBy = updatedBy;

        if (FailedLoginAttempts >= maxFailedAttempts)
        {
            LockedUntil = now.Add(lockDuration);
            FailedLoginAttempts = 0;
            return LoginFailureOutcome.LockedOut;
        }

        return LoginFailureOutcome.InvalidCredentials;
    }

    public void RecordLoginSuccess(DateTime now, long? updatedBy = null)
    {
        FailedLoginAttempts = 0;
        LockedUntil = null;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    public void ChangePassword(string newPasswordHash, DateTime now, long? updatedBy = null)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            throw new ArgumentException("密码哈希不能为空。", nameof(newPasswordHash));

        PasswordHash = newPasswordHash;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    public void Enable(DateTime now, long? updatedBy = null)
    {
        IsEnabled = true;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    public void Disable(DateTime now, long? updatedBy = null)
    {
        IsEnabled = false;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    public void BindUser(long? userId, DateTime now, long? updatedBy = null)
    {
        UserId = userId;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// 软删除：改写用户名为 <c>{UserName}#d{Id}</c>，释放原用户名唯一索引。
    /// </summary>
    public void SoftDelete(DateTime now, long? deletedBy = null)
    {
        if (IsDeleted) return;
        if (Id <= 0) throw new InvalidOperationException("尚未持久化，无法软删除。");

        IsDeleted = true;
        DeletedAt = now;
        DeletedBy = deletedBy;

        // 释放唯一索引：Id 唯一 → 改写后仍唯一
        var suffix = $"#d{Id}";
        var maxBaseLen = 64 - suffix.Length;
        UserName = UserName.Length > maxBaseLen
            ? UserName[..maxBaseLen] + suffix
            : UserName + suffix;

        UpdatedAt = now;
        UpdatedBy = deletedBy;
    }
}
