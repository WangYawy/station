using Microsoft.Extensions.Logging;
using Station.Application.Departments;
using Station.Application.Recorders.Commands;
using Station.Application.Users;
using Station.Crypto;
using Station.Data.IdGeneration;
using Station.Device.Detection;
using Station.Domain.Abstractions;
using Station.Domain.Recorders;

namespace Station.Application.Recorders;

/// <summary>
/// 记录仪台账应用服务实现。
/// 
/// 【依赖方向】
///   - IRecorderRepository：短连接 UoW 写
///   - IRecorderQueryService / IUserQueryService / IDeptQueryService：读侧校验
///   - IRecorderBindingFile：绑定文件业务（内部走 IDeviceFileSystem）
///   - IDevicePresenceService：找在线设备（写文件目标）
///   - IDomainEventBus：领域事件
/// </summary>
public sealed class RecorderService : IRecorderService
{
    private readonly IRecorderRepository _recorders;
    private readonly IRecorderQueryService _recorderQuery;
    private readonly IUserQueryService _userQuery;
    private readonly IDeptQueryService _deptQuery;
    private readonly IRecorderBindingFile _bindingFile;
    private readonly IDevicePresenceService _presence;
    private readonly IIdGenerator _idGen;
    private readonly IDomainEventBus _events;
    private readonly ILogger<RecorderService> _logger;

    public RecorderService(
        IRecorderRepository recorders,
        IRecorderQueryService recorderQuery,
        IUserQueryService userQuery,
        IDeptQueryService deptQuery,
        IRecorderBindingFile bindingFile,
        IDevicePresenceService presence,
        IIdGenerator idGen,
        IDomainEventBus events,
        ILogger<RecorderService> logger)
    {
        _recorders = recorders;
        _recorderQuery = recorderQuery;
        _userQuery = userQuery;
        _deptQuery = deptQuery;
        _bindingFile = bindingFile;
        _presence = presence;
        _idGen = idGen;
        _events = events;
        _logger = logger;
    }

    // =========================================================
    // CRUD
    // =========================================================

    public async Task<long> CreateAsync(CreateRecorderCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (string.IsNullOrWhiteSpace(cmd.Name))
            throw new ArgumentException("记录仪名称不能为空", nameof(cmd));
        if (string.IsNullOrWhiteSpace(cmd.SerialNumber))
            throw new ArgumentException("序列号不能为空", nameof(cmd));

        var serial = cmd.SerialNumber.Trim();

        // 唯一性校验：序列号不能重复
        var existing = await _recorderQuery.GetBySerialAsync(serial, ct);
        if (existing is not null)
            throw new InvalidOperationException($"序列号 {serial} 已存在");

        var id = _idGen.NewId();
        var now = DateTime.Now;

        var recorder = Recorder.Create(
            id,
            serialNumber: serial,
            code: cmd.Code?.Trim() ?? string.Empty,
            name: cmd.Name.Trim(),
            model: cmd.Model?.Trim() ?? string.Empty,
            protocol: cmd.Protocol,
            isAuthorized: cmd.IsAuthorized,
            now: now,
            createdBy: cmd.CreatedBy);

        await _recorders.AddAsync(recorder, ct);
        await PublishAndClearAsync(recorder, ct);

        _logger.LogInformation("记录仪台账新增：{Id} {Serial} {Name}",
            id, serial, cmd.Name);

        return id;
    }

    public async Task UpdateAsync(UpdateRecorderCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (string.IsNullOrWhiteSpace(cmd.Name))
            throw new ArgumentException("记录仪名称不能为空", nameof(cmd));

        var recorder = await _recorders.GetByIdAsync(cmd.Id, ct)
            ?? throw new InvalidOperationException($"记录仪 {cmd.Id} 不存在");

        if (recorder.IsDeleted)
            throw new InvalidOperationException("记录仪已删除，无法修改");

        var now = DateTime.Now;

        recorder.ChangeName(cmd.Name.Trim(), now, cmd.UpdatedBy);
        recorder.ChangeCode(cmd.Code?.Trim() ?? string.Empty, now, cmd.UpdatedBy);
        recorder.ChangeModel(cmd.Model?.Trim() ?? string.Empty, now, cmd.UpdatedBy);
        recorder.ChangeProtocol(cmd.Protocol, now, cmd.UpdatedBy);

        // 注意：这里只改台账绑定字段，不写设备内绑定文件。
        // 写文件必须走 BindAsync，避免台账与设备状态不一致。
        if (recorder.BoundUserId != cmd.BoundUserId || recorder.DeptId != cmd.DeptId)
            recorder.BindTo(cmd.BoundUserId, cmd.DeptId, now, cmd.UpdatedBy);

        if (cmd.IsAuthorized) recorder.Authorize(now, cmd.UpdatedBy);
        else recorder.RevokeAuthorization(now, cmd.UpdatedBy);

        if (cmd.IsActive) recorder.Activate(now, cmd.UpdatedBy);
        else recorder.Deactivate(now, cmd.UpdatedBy);

        await _recorders.UpdateAsync(recorder, ct);
        await PublishAndClearAsync(recorder, ct);

        _logger.LogInformation("记录仪台账更新：{Id}", cmd.Id);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var recorder = await _recorders.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"记录仪 {id} 不存在");

        if (recorder.IsDeleted) return;

        // 已绑定的记录仪必须先解绑
        if (recorder.BoundUserId.HasValue || recorder.DeptId.HasValue)
            throw new InvalidOperationException("记录仪已绑定用户/部门，请先解绑");

        recorder.MarkDeleted(DateTime.Now);
        await _recorders.UpdateAsync(recorder, ct);
        await PublishAndClearAsync(recorder, ct);

        _logger.LogInformation("记录仪台账删除：{Id}", id);
    }

    // =========================================================
    // 状态
    // =========================================================

    public async Task ActivateAsync(long id, CancellationToken ct = default)
    {
        var recorder = await _recorders.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"记录仪 {id} 不存在");

        if (recorder.IsDeleted)
            throw new InvalidOperationException("记录仪已删除，无法激活");

        if (recorder.IsActive) return;

        recorder.Activate(DateTime.Now);
        await _recorders.UpdateAsync(recorder, ct);
        await PublishAndClearAsync(recorder, ct);

        _logger.LogInformation("记录仪激活：{Id}", id);
    }

    public async Task DeactivateAsync(long id, CancellationToken ct = default)
    {
        var recorder = await _recorders.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"记录仪 {id} 不存在");

        if (recorder.IsDeleted)
            throw new InvalidOperationException("记录仪已删除，无法停用");

        if (!recorder.IsActive) return;

        recorder.Deactivate(DateTime.Now);
        await _recorders.UpdateAsync(recorder, ct);
        await PublishAndClearAsync(recorder, ct);

        _logger.LogInformation("记录仪停用：{Id}", id);
    }

    // =========================================================
    // 绑定
    // =========================================================

    public async Task<BindResult> BindAsync(BindRecorderCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        // 1. 加载台账
        var recorder = await _recorders.GetByIdAsync(cmd.RecorderId, ct);
        if (recorder is null)
            return BindResult.Fail(BindStatus.RecorderNotFound, "记录仪不在台账");

        if (recorder.IsDeleted)
            return BindResult.Fail(BindStatus.RecorderDeleted, "记录仪已删除");

        if (!recorder.IsActive)
            return BindResult.Fail(BindStatus.RecorderInactive, "记录仪已停用");

        // 2. 校验用户
        var user = await _userQuery.GetByIdAsync(cmd.UserId, ct);
        if (user is null || !user.IsActive)
            return BindResult.Fail(BindStatus.UserInactive, "用户不在册或已禁用");
        if (string.IsNullOrWhiteSpace(user.UserNo))
            return BindResult.Fail(BindStatus.UserInactive, "用户工号为空，无法写入绑定文件");

        // 3. 校验部门
        var dept = await _deptQuery.GetByIdAsync(cmd.DeptId, ct);
        if (dept is null || !dept.IsActive)
            return BindResult.Fail(BindStatus.DeptInactive, "部门不在册或已禁用");
        if (string.IsNullOrWhiteSpace(dept.Code))
            return BindResult.Fail(BindStatus.DeptInactive, "部门编码为空，无法写入绑定文件");

        // 4. 找在线设备（按序列号匹配）
        var device = FindOnlineDevice(recorder.SerialNumber);
        if (device is null)
            return BindResult.Fail(BindStatus.DeviceNotOnline,
                "记录仪未接入，请插入设备后重试");

        // 5. 写绑定文件（MAC 由 RecorderBindingFile.WriteAsync 内部计算）
        try
        {
            var binding = new BindingInfo(
                DeviceSerial: recorder.SerialNumber,
                DeviceModel: recorder.Model,
                UserNo: user.UserNo,
                UserName: user.Name,
                DeptCode: dept.Code,
                DeptName: dept.Name,
                BoundAt: DateTime.Now,
                Signature: "");

            await _bindingFile.WriteAsync(device, binding, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "绑定文件写入失败：Recorder={RecorderId}", cmd.RecorderId);
            return BindResult.Fail(BindStatus.WriteFailed, $"写绑定文件失败：{ex.Message}");
        }

        // 6. 更新台账
        recorder.BindTo(cmd.UserId, cmd.DeptId, DateTime.Now, cmd.UpdatedBy);
        await _recorders.UpdateAsync(recorder, ct);
        await PublishAndClearAsync(recorder, ct);

        _logger.LogInformation(
            "记录仪绑定成功：Recorder={RecorderId} User={UserId} Dept={DeptId} Device={DeviceKey}",
            cmd.RecorderId, cmd.UserId, cmd.DeptId, device.Key);

        return BindResult.Ok();
    }

    // =========================================================
    // 解绑
    // =========================================================

    public async Task<UnbindResult> UnbindAsync(long recorderId, CancellationToken ct = default)
    {
        // 1. 加载台账
        var recorder = await _recorders.GetByIdAsync(recorderId, ct);
        if (recorder is null)
            return UnbindResult.Fail(UnbindStatus.RecorderNotFound, "记录仪不在台账");

        if (!recorder.BoundUserId.HasValue && !recorder.DeptId.HasValue)
            return UnbindResult.Fail(UnbindStatus.NotBound, "记录仪未绑定");

        // 2. 尝试删除绑定文件（设备在线时）
        var device = FindOnlineDevice(recorder.SerialNumber);
        if (device is not null)
        {
            try
            {
                _bindingFile.Delete(device);
            }
            catch (Exception ex)
            {
                // 删除失败不阻塞台账解绑；仅日志告警
                _logger.LogWarning(ex,
                    "删除绑定文件失败（设备在线但文件删除异常）：Recorder={RecorderId}",
                    recorderId);
            }
        }
        else
        {
            // 设备不在线时仍允许台账解绑。
            // 下次接入会因绑定文件残留而被识别为 Mismatch，
            // 届时管理员可在设备在线时重新绑定覆盖。
            _logger.LogInformation(
                "记录仪未在线，仅解绑台账：Recorder={RecorderId}", recorderId);
        }

        // 3. 清空台账绑定
        recorder.Unbind(DateTime.Now);
        await _recorders.UpdateAsync(recorder, ct);
        await PublishAndClearAsync(recorder, ct);

        _logger.LogInformation("记录仪解绑成功：Recorder={RecorderId}", recorderId);

        return UnbindResult.Ok();
    }

    // =========================================================
    // 内部
    // =========================================================

    /// <summary>
    /// 按序列号匹配在线设备。
    /// UMS / MTP 的序列号可能为空，此时返回 null。
    /// 
    /// 注意：当前 DetectedDevice.Serial 在 UMS 场景下常为 null。
    /// 若业务需要"UMS 也能绑定"，后续可在 DetectedDevice 上补充
    /// 卷序列号（VolumeSerialNumber），并在此处做二次匹配。
    /// </summary>
    private DetectedDevice? FindOnlineDevice(string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return null;

        var connected = _presence.GetConnectedDevices();
        return connected.FirstOrDefault(d =>
            !string.IsNullOrEmpty(d.Serial)
            && string.Equals(d.Serial, serialNumber, StringComparison.OrdinalIgnoreCase));
    }

    private async Task PublishAndClearAsync(Recorder recorder, CancellationToken ct = default)
    {
        if (recorder.DomainEvents.Count == 0) return;

        var events = recorder.DomainEvents.ToList();
        recorder.ClearDomainEvents();
        await _events.PublishRangeAsync(events, ct);
    }
}
