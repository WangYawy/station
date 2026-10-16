using Station.Application.Departments;
using Station.Application.Recorders.Commands;
using Station.Application.Users;
using Station.Contracts;
using Station.Crypto;
using Station.Data.IdGeneration;
using Station.Domain.Recorders;

namespace Station.Application.Recorders;

public sealed class RecorderService : IRecorderService
{
    private readonly IRecorderRepository _recorders;
    private readonly IUserQueryService _userQuery;
    private readonly IDeptQueryService _deptQuery;
    private readonly RecorderBindingFile _bindingFile;
    private readonly IIdGenerator _idGenerator;

    public RecorderService(
        IRecorderRepository recorders,
        IUserQueryService userQuery,
        IDeptQueryService deptQuery,
        RecorderBindingFile bindingFile,
        IIdGenerator idGenerator)
    {
        _recorders = recorders;
        _userQuery = userQuery;
        _deptQuery = deptQuery;
        _bindingFile = bindingFile;
        _idGenerator = idGenerator;
    }

    // 删除 GetRecordersAsync —— 读侧走 QueryService

    public async Task<long> RegisterAsync(
        string serialNumber, string code, string name, string model, ProtocolType protocol,
        bool isAuthorized, CancellationToken ct = default)
    {
        if (await _recorders.ExistsSerialAsync(serialNumber, ct))
            throw new InvalidOperationException($"记录仪 {serialNumber} 已存在");

        var recorder = Recorder.Create(
            _idGenerator.NewId(), serialNumber, code, name, model, protocol,
            isAuthorized, DateTime.Now);

        await _recorders.AddAsync(recorder, ct);
        return recorder.Id;
    }

    public async Task<OpResult> UpdateAsync(
        UpdateRecorderCommand command, CancellationToken ct = default)
    {
        var recorder = await _recorders.GetByIdAsync(command.Id, ct);
        if (recorder is null) return new OpResult(false, "记录仪不存在");

        var now = DateTime.Now;
        recorder.ChangeModel(command.Model, now);
        recorder.ChangeProtocol(command.Protocol, now);

        if (recorder.BoundUserId != command.BoundUserId || recorder.DeptId != command.DeptId)
            recorder.BindTo(command.BoundUserId, command.DeptId, now);

        if (command.IsAuthorized) recorder.Authorize(now);
        else recorder.RevokeAuthorization(now);

        if (command.IsActive) recorder.Activate(now);
        else recorder.Deactivate(now);

        await _recorders.UpdateAsync(recorder, ct);
        return new OpResult(true);
    }

    public async Task WriteBindingAsync(
        string serialNumber, long? userId, long? deptId,
        string recorderRootPath, CancellationToken ct = default)
    {
        var recorder = await _recorders.GetBySerialAsync(serialNumber, ct)
            ?? throw new InvalidOperationException($"记录仪 {serialNumber} 不在台账");

        recorder.BindTo(userId, deptId, DateTime.Now);
        await _recorders.UpdateAsync(recorder, ct);

        var user = userId is null ? null : await _userQuery.GetByIdAsync(userId.Value, ct);
        var dept = deptId is null ? null : await _deptQuery.GetByIdAsync(deptId.Value, ct);

        var binding = new BindingInfo(
            recorder.SerialNumber, recorder.Model,
            user?.UserNo ?? string.Empty, user?.Name ?? string.Empty,
            dept?.Code ?? string.Empty, dept?.Name ?? string.Empty,
            DateTime.Now, Signature: string.Empty);

        await _bindingFile.WriteAsync(recorderRootPath, binding, ct);
    }

    public async Task UnbindAsync(
        string serialNumber, string recorderRootPath, CancellationToken ct = default)
    {
        var recorder = await _recorders.GetBySerialAsync(serialNumber, ct);
        if (recorder is not null)
        {
            recorder.Unbind(DateTime.Now);
            await _recorders.UpdateAsync(recorder, ct);
        }
        _bindingFile.Delete(recorderRootPath);
    }
}
