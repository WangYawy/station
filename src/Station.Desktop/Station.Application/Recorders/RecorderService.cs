using Station.Contracts;
using Station.Domain.Entities;
using Station.Data.Repositories;
using Station.Data.IdGeneration;

namespace Station.Application.Recorders;

/// <summary>
/// 记录仪台账服务：注册、更新、绑定/解绑。
/// 
/// 【绑定流程】
///   调用 RecorderBindingFile.WriteAsync 时，内部会：
///     1) 读 CryptoUsage.RecorderBinding 策略（默认 HMAC-SM3）；
///     2) 从 IBindingSecretProvider 取密钥（主密钥 HKDF 派生）；
///     3) 计算 MAC；
///     4) 写入 station_bind.ini。
///   调用方不需要手动算签名。
/// </summary>
public sealed class RecorderService : IRecorderService
{
    private readonly IRepository<Recorder> _recorders;
    private readonly IRepository<User> _users;
    private readonly IRepository<Dept> _depts;
    private readonly RecorderBindingFile _bindingFile;
    private readonly IIdGenerator _idGenerator;

    public RecorderService(
        IRepository<Recorder> recorders,
        IRepository<User> users,
        IRepository<Dept> depts,
        RecorderBindingFile bindingFile,
        IIdGenerator idGenerator)
    {
        _recorders = recorders;
        _users = users;
        _depts = depts;
        _bindingFile = bindingFile;
        _idGenerator = idGenerator;
    }

    /// <summary>
    /// 台账查询
    /// </summary>
    public async Task<IReadOnlyList<RecorderDto>> GetRecordersAsync()
    {
        var list = await _recorders.GetListAsync(r => r.IsActive);
        return list.OrderBy(r => r.SerialNumber).Select(ToDto).ToList();
    }

    /// <summary>
    /// 注册
    /// </summary>
    public async Task<RecorderDto> RegisterAsync(
        string serialNumber,
        string model,
        ProtocolType protocol,
        bool isAuthorized)
    {
        if (await _recorders.IsAnyAsync(r => r.SerialNumber == serialNumber))
        {
            throw new InvalidOperationException($"记录仪 {serialNumber} 已存在");
        }

        var recorder = new Recorder
        {
            Id = _idGenerator.NewId(),
            SerialNumber = serialNumber,
            Model = model,
            Protocol = protocol,
            IsAuthorized = isAuthorized,
            IsActive = true
        };
        await _recorders.InsertAsync(recorder);
        return ToDto(recorder);
    }

    /// <summary>
    /// 更新
    /// </summary>
    /// <param name="dto"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task<RecorderDto> UpdateAsync(RecorderDto dto)
    {
        var recorder = await _recorders.GetByIdAsync(dto.Id!.Value)
                       ?? throw new InvalidOperationException("记录仪不存在");
        recorder.SerialNumber = dto.SerialNumber;
        recorder.Model = dto.Model;
        recorder.Protocol = dto.Protocol;
        recorder.BoundUserId = dto.BoundUserId;
        recorder.DeptId = dto.DeptId;
        recorder.IsAuthorized = dto.IsAuthorized;
        recorder.IsActive = dto.IsActive;
        await _recorders.UpdateAsync(recorder);
        return ToDto(recorder);
    }

    /// <summary>
    /// 绑定记录仪到用户/部门，并写入根目录的 station_bind.ini。
    /// </summary>
    public async Task WriteBindingAsync(
        string serialNumber,
        long? userId,
        long? deptId,
        string recorderRootPath,
        CancellationToken ct = default)
    {
        // 1) 更新台账
        var recorder = await _recorders.FirstAsync(r => r.SerialNumber == serialNumber)
                       ?? throw new InvalidOperationException($"记录仪 {serialNumber} 不在台账");

        recorder.BoundUserId = userId;
        recorder.DeptId = deptId;
        await _recorders.UpdateAsync(recorder);

        // 2) 组装绑定信息
        var user = userId is null ? null : await _users.GetByIdAsync(userId.Value);
        var dept = deptId is null ? null : await _depts.GetByIdAsync(deptId.Value);
        var boundAt = DateTime.Now;

        var binding = new BindingInfo(
            recorder.SerialNumber,
            recorder.Model,
            user?.UserNo ?? string.Empty,
            user?.Name ?? string.Empty,
            dept?.Code ?? string.Empty,
            dept?.Name ?? string.Empty,
            boundAt,
            Signature: string.Empty);   // Write 时由 WriteAsync 内部算并写入文件

        // 3) 写文件（内部自动算 MAC）
        await _bindingFile.WriteAsync(recorderRootPath, binding, ct);
    }

    /// <summary>
    ///  解绑：删除 station_bind.ini
    /// </summary>
    /// <param name="serialNumber"></param>
    /// <param name="recorderRootPath"></param>
    /// <returns></returns>
    public async Task UnbindAsync(string serialNumber, string recorderRootPath)
    {
        var recorder = await _recorders.FirstAsync(r => r.SerialNumber == serialNumber);
        if (recorder is not null)
        {
            recorder.BoundUserId = null;
            recorder.DeptId = null;
            await _recorders.UpdateAsync(recorder);
        }

        _bindingFile.Delete(recorderRootPath);
    }

    private static RecorderDto ToDto(Recorder r) => new(
        r.Id, r.SerialNumber, r.Model, r.Protocol,
        r.BoundUserId, r.DeptId, r.IsAuthorized, r.IsActive);
}
