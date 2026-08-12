using Station.Contracts;
using Station.Domain.Entities;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Recorders;
using Station.Infrastructure.Repositories;

namespace Station.Application.Recorders;

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

    public async Task<IReadOnlyList<RecorderDto>> GetRecordersAsync()
    {
        var list = await _recorders.GetListAsync(r => r.IsActive);
        return list.OrderBy(r => r.SerialNumber).Select(ToDto).ToList();
    }

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
            Id = _idGenerator.NextId(),
            SerialNumber = serialNumber,
            Model = model,
            Protocol = protocol,
            IsAuthorized = isAuthorized,
            IsActive = true
        };
        await _recorders.InsertAsync(recorder);
        return ToDto(recorder);
    }

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

    public async Task WriteBindingAsync(string serialNumber, long? userId, long? deptId, string recorderRootPath)
    {
        var recorder = await _recorders.FirstAsync(r => r.SerialNumber == serialNumber)
                       ?? throw new InvalidOperationException($"记录仪 {serialNumber} 不在台账");
        recorder.BoundUserId = userId;
        recorder.DeptId = deptId;
        await _recorders.UpdateAsync(recorder);

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
            string.Empty);
        var signature = _bindingFile.ComputeSignature(
            binding.RecorderSerial, binding.RecorderModel, binding.UserNo, binding.UserName,
            binding.DeptCode, binding.DeptName, boundAt);
        _bindingFile.Write(recorderRootPath, binding with { Signature = signature });
    }

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
        r.Id, r.SerialNumber, r.Model, r.Protocol, r.BoundUserId, r.DeptId, r.IsAuthorized, r.IsActive);
}
