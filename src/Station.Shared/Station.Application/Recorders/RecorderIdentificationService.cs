using Station.Application.Alerts;
using Station.Application.Collecting;
using Station.Contracts;
using Station.Domain.Entities;
using Station.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Station.Domain.Collecting;

namespace Station.Application.Recorders;

public sealed class RecorderIdentificationService : IRecorderIdentificationService
{
    private readonly IRepository<Recorder> _recorders;
    private readonly IRepository<User> _users;
    private readonly IRepository<Dept> _depts;
    private readonly IAlertService _alerts;
    private readonly RecorderBindingFile _bindingFile;
    private readonly ILogger<RecorderIdentificationService> _logger;

    public RecorderIdentificationService(
        IRepository<Recorder> recorders,
        IRepository<User> users,
        IRepository<Dept> depts,
        IAlertService alerts,
        RecorderBindingFile bindingFile,
        ILogger<RecorderIdentificationService> logger)
    {
        _recorders = recorders;
        _users = users;
        _depts = depts;
        _alerts = alerts;
        _bindingFile = bindingFile;
        _logger = logger;
    }

    /// <summary>
    /// 设备识别
    /// </summary>
    /// <param name="device">设备</param>
    /// <param name="recorderRootPath">根目录</param>
    /// <returns></returns>
    public async Task<RecorderIdentifyResult> IdentifyAsync(CollectDeviceInfo device, string recorderRootPath)
    {
        var binding = _bindingFile.Read(recorderRootPath);

        // 第一层：无 ini = 未绑定
        if (binding is null)
        {
            _logger.LogWarning("记录仪 {Recorder} 未绑定（无 station_bind.ini），拒绝采集", device.Serial ?? device.Name);
            await WriteAlertAsync(AlertType.BindingInvalid, AlertLevel.Warning, "记录仪未绑定",
                $"记录仪 {device.Serial ?? device.Name} 根目录无绑定文件，拒绝采集",
                device.Serial);
            return new RecorderIdentifyResult(RecorderIdentifyStatus.NoBinding, null, null, "未绑定：拒绝采集");
        }

        // 第二层：ini 校验失败 = 疑似篡改
        if (!_bindingFile.VerifySignature(binding))
        {
            _logger.LogError("记录仪 {Recorder} 绑定文件 SM3 校验失败（疑似篡改）", binding.RecorderSerial);
            await WriteAlertAsync(AlertType.BindingInvalid, AlertLevel.Critical, "绑定文件疑似篡改",
                $"记录仪 {binding.RecorderSerial} 绑定文件 SM3 校验失败",
                binding.RecorderSerial);
            return new RecorderIdentifyResult(RecorderIdentifyStatus.InvalidSignature, null, null, "疑似篡改：拒绝采集");
        }

        // 第三层：编号不在台账/白名单 = 非授权接入
        var recorder = await _recorders.FirstAsync(r => r.SerialNumber == binding.RecorderSerial);
        if (recorder is null || !recorder.IsAuthorized)
        {
            _logger.LogWarning("记录仪 {Recorder} 非授权接入（不在白名单或未授权）", binding.RecorderSerial);
            await WriteAlertAsync(AlertType.UnauthorizedAccess, AlertLevel.Critical, "非授权接入",
                $"记录仪 {binding.RecorderSerial} 不在白名单或未授权，拒绝采集",
                binding.RecorderSerial);
            return new RecorderIdentifyResult(RecorderIdentifyStatus.UnknownRecorder, null, null, "非授权接入：拒绝采集");
        }

        // 台账绑定一致性：ini 与台账不一致时按台账自动重写 ini
        var user = string.IsNullOrEmpty(binding.UserNo) ? null : await _users.FirstAsync(u => u.UserNo == binding.UserNo);
        var dept = string.IsNullOrEmpty(binding.DeptCode) ? null : await _depts.FirstAsync(d => d.Code == binding.DeptCode);
        var userId = recorder.BoundUserId ?? user?.Id;
        var deptId = recorder.DeptId ?? dept?.Id;

        if ((recorder.BoundUserId is { } boundUser && user is not null && boundUser != user.Id) ||
            (recorder.DeptId is { } boundDept && dept is not null && boundDept != dept.Id))
        {
            var boundUserEntity = recorder.BoundUserId is null ? null : await _users.GetByIdAsync(recorder.BoundUserId.Value);
            var boundDeptEntity = recorder.DeptId is null ? null : await _depts.GetByIdAsync(recorder.DeptId.Value);
            var boundAt = DateTime.Now;
            var rewritten = new BindingInfo(
                recorder.SerialNumber,
                recorder.Model,
                boundUserEntity?.UserNo ?? string.Empty,
                boundUserEntity?.Name ?? string.Empty,
                boundDeptEntity?.Code ?? string.Empty,
                boundDeptEntity?.Name ?? string.Empty,
                boundAt,
                string.Empty);
            var signature = _bindingFile.ComputeSignature(
                rewritten.RecorderSerial, rewritten.RecorderModel, rewritten.UserNo, rewritten.UserName,
                rewritten.DeptCode, rewritten.DeptName, boundAt);
            _bindingFile.Write(recorderRootPath, rewritten with { Signature = signature });
        }

        return new RecorderIdentifyResult(
            RecorderIdentifyStatus.Bound,
            userId,
            deptId,
            $"已绑定：{user?.Name ?? "未关联用户"}（{dept?.Name ?? "未关联部门"}）");
    }

    /// <summary>
    /// 报警信息
    /// </summary>
    private async Task WriteAlertAsync(AlertType type, AlertLevel level, string title, string detail, string? source)
    {
        await _alerts.WriteAsync(new Alert
        {
            Type = type,
            Level = level,
            Title = title,
            Detail = detail,
            Source = source
        });
    }
}
