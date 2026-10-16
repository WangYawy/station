using Station.Contracts;

namespace Station.Domain.Recorders;

/// <summary>
/// 记录仪台账聚合根（含白名单）。
/// 序列号为跨端自然键，创建后不可变。
/// </summary>
public sealed class Recorder
{
    public long Id { get; private set; }
    public string SerialNumber { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public ProtocolType Protocol { get; private set; }
    public long? BoundUserId { get; private set; }
    public long? DeptId { get; private set; }
    public bool IsAuthorized { get; private set; }
    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public long? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public long? UpdatedBy { get; private set; }

    private Recorder() { }

    // =========================================================
    // 工厂
    // =========================================================

    public static Recorder Create(
        long id,
        string serialNumber,
        string code,
        string name,
        string model,
        ProtocolType protocol,
        bool isAuthorized,
        DateTime now,
        long? createdBy = null)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (string.IsNullOrWhiteSpace(serialNumber))
            throw new ArgumentException("记录仪序列号不能为空。", nameof(serialNumber));
        if (serialNumber.Length > 64)
            throw new ArgumentException("记录仪序列号长度不能超过 64。", nameof(serialNumber));

        return new Recorder
        {
            Id = id,
            SerialNumber = serialNumber,
            Code = code,
            Name = name,
            Model = model ?? string.Empty,
            Protocol = protocol,
            IsAuthorized = isAuthorized,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = createdBy
        };
    }

    public static Recorder Restore(
        long id, string serialNumber, string code, string name, string model, ProtocolType protocol,
        long? boundUserId, long? deptId, bool isAuthorized, bool isActive,
        DateTime createdAt, long? createdBy, DateTime? updatedAt, long? updatedBy)
        => new()
        {
            Id = id,
            SerialNumber = serialNumber,
            Code = code,
            Name = name,
            Model = model,
            Protocol = protocol,
            BoundUserId = boundUserId,
            DeptId = deptId,
            IsAuthorized = isAuthorized,
            IsActive = isActive,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            UpdatedAt = updatedAt,
            UpdatedBy = updatedBy
        };

    // =========================================================
    // 行为
    // =========================================================

    public void ChangeModel(string model, DateTime now, long? updatedBy = null)
    {
        Model = model ?? string.Empty;
        Touch(now, updatedBy);
    }

    public void ChangeCode(string code, DateTime now, long? updatedBy = null)
    {
        Code = code ?? string.Empty;
        Touch(now, updatedBy);
    }
    public void ChangeName(string name, DateTime now, long? updatedBy = null)
    {
        Name = name ?? string.Empty;
        Touch(now, updatedBy);
    }

    public void ChangeProtocol(ProtocolType protocol, DateTime now, long? updatedBy = null)
    {
        if (Protocol == protocol) return;
        Protocol = protocol;
        Touch(now, updatedBy);
    }

    /// <summary>绑定到用户/部门；userId/deptId 全为 null 等价于解绑。</summary>
    public void BindTo(long? userId, long? deptId, DateTime now, long? updatedBy = null)
    {
        if (BoundUserId == userId && DeptId == deptId) return;
        BoundUserId = userId;
        DeptId = deptId;
        Touch(now, updatedBy);
    }

    public void Unbind(DateTime now, long? updatedBy = null)
    {
        if (BoundUserId is null && DeptId is null) return;
        BoundUserId = null;
        DeptId = null;
        Touch(now, updatedBy);
    }

    public void Authorize(DateTime now, long? updatedBy = null)
    {
        if (IsAuthorized) return;
        IsAuthorized = true;
        Touch(now, updatedBy);
    }

    public void RevokeAuthorization(DateTime now, long? updatedBy = null)
    {
        if (!IsAuthorized) return;
        IsAuthorized = false;
        Touch(now, updatedBy);
    }

    public void Activate(DateTime now, long? updatedBy = null)
    {
        if (IsActive) return;
        IsActive = true;
        Touch(now, updatedBy);
    }

    public void Deactivate(DateTime now, long? updatedBy = null)
    {
        if (!IsActive) return;
        IsActive = false;
        Touch(now, updatedBy);
    }

    /// <summary>是否允许采集（白名单 + 启用）。</summary>
    public bool CanCollect() => IsAuthorized && IsActive;

    private void Touch(DateTime now, long? updatedBy)
    {
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }
}
