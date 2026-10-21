using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Departments;
using Station.Application.Recorders.Dtos;
using Station.Application.Users;
using Station.Crypto;
using Station.Device.Detection;

namespace Station.Application.Recorders;

/// <summary>
/// 记录仪识别服务实现。
///
/// 【四层校验流程】
///   0) 总开关             EnableBinding=false → 直接 Matched（不读文件）
///   1) 绑定文件存在性     无 station_bind.ini → NotFound
///   2) MAC 验证            EnableSignature=true 时才执行 → Legacy / Tampered
///   3) 设备级台账校验      始终执行 → RecorderMissing / Tampered
///   4) 用户/部门校验       EnableBindingValidation=true 时才执行 → UserInactive / DeptInactive / Mismatch
/// </summary>
public sealed class RecorderIdentificationService : IRecorderIdentificationService
{
    private readonly IRecorderBindingFile _bindingFile;
    private readonly IRecorderQueryService _recorderQuery;
    private readonly IUserQueryService _userQuery;
    private readonly IDeptQueryService _deptQuery;
    private readonly BindingOptions _options;
    private readonly ILogger<RecorderIdentificationService> _logger;

    public RecorderIdentificationService(
        IOptions<BindingOptions> options,
        IRecorderBindingFile bindingFile,
        IRecorderQueryService recorderQuery,
        IUserQueryService userQuery,
        IDeptQueryService deptQuery,
        ILogger<RecorderIdentificationService> logger)
    {
        _options = options.Value;
        _bindingFile = bindingFile;
        _recorderQuery = recorderQuery;
        _userQuery = userQuery;
        _deptQuery = deptQuery;
        _logger = logger;
    }

    public async Task<RecorderIdentifyResult> IdentifyAsync(
        DetectedDevice device, CancellationToken ct = default)
    {
        // 占位 BoundDevice（识别过程中的中间态，无业务归属）
        BoundDevice MakeBound(long? userId = null, long? deptId = null,
                              long? recorderId = null, string? userNo = null, string? deptCode = null, string? recorderCode = null, string? userName = null, string? deptName = null)
            => new(device, userId, deptId, recorderId, userNo, deptCode, recorderCode, userName, deptName);

        // ---- 第 0 层：总开关 ----
        if (!_options.EnableBinding)
            return RecorderIdentifyResult.Matched(MakeBound(), null!, null!);

        // ---- 第 1 层：读绑定文件 ----
        ct.ThrowIfCancellationRequested();
        BindingInfo? binding;
        try { binding = _bindingFile.Read(device); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取绑定文件失败：{Root}", device.Root);
            return RecorderIdentifyResult.NotFound(MakeBound(), "绑定文件读取失败");
        }

        if (binding is null)
            return RecorderIdentifyResult.NotFound(MakeBound(), "设备缺失绑定文件");

        // ---- 第 2 层：MAC 验证 ----
        ct.ThrowIfCancellationRequested();
        if (_options.EnableSignature)
        {
            if (string.IsNullOrEmpty(binding.Signature))
                return RecorderIdentifyResult.Legacy(MakeBound(), binding, "绑定文件格式已升级，请重新绑定");

            bool sigOk;
            try { sigOk = await _bindingFile.VerifySignatureAsync(device, ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "验签异常：{Serial}", device.Serial);
                return RecorderIdentifyResult.Tampered(MakeBound(), binding, "签名验证异常");
            }

            if (!sigOk)
                return RecorderIdentifyResult.Tampered(MakeBound(), binding, "绑定文件被篡改或密钥不匹配");
        }

        // ---- 第 3 层：台账校验 ----
        ct.ThrowIfCancellationRequested();
        var recorderDto = await _recorderQuery.GetByCodeAsync(binding.DeviceSerial, ct);
        if (recorderDto is null)
            return RecorderIdentifyResult.RecorderMissing(MakeBound(), binding, "记录仪不在台账");
        if (!recorderDto.IsActive)
            return RecorderIdentifyResult.RecorderMissing(MakeBound(), binding, "记录仪已停用");

        // 用户/部门 ID 从台账取
        var boundUserId = recorderDto.BoundUserId;
        var boundDeptId = recorderDto.DeptId;

        // 台账与绑定文件的一致性（序列号）
        if (!string.Equals(recorderDto.Code, binding.DeviceSerial, StringComparison.OrdinalIgnoreCase))
            return RecorderIdentifyResult.Mismatch(
                MakeBound(boundUserId, boundDeptId, recorderDto.Id, binding.UserNo, binding.DeptCode, recorderDto.Code, binding.UserName, binding.DeptName),
                binding, "绑定信息与台账不一致");

        // ---- 第 4 层：用户/部门校验 ----
        if (!_options.EnableBindingValidation)
            return RecorderIdentifyResult.Matched(
                MakeBound(boundUserId, boundDeptId, recorderDto.Id, binding.UserNo, binding.DeptCode, recorderDto.Code, binding.UserName, binding.DeptName),
                binding, recorderDto);

        ct.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(binding.UserNo))
        {
            var userDto = await _userQuery.GetByUserNoAsync(binding.UserNo, ct);
            if (userDto is null)
                return RecorderIdentifyResult.UserInactive(
                    MakeBound(boundUserId, boundDeptId, recorderDto.Id, binding.UserNo, binding.DeptCode, recorderDto.Code, binding.UserName, binding.DeptName),
                    binding, "绑定用户不在册");
            if (!userDto.IsActive)
                return RecorderIdentifyResult.UserInactive(
                    MakeBound(boundUserId, boundDeptId, recorderDto.Id, binding.UserNo, binding.DeptCode, recorderDto.Code, binding.UserName, binding.DeptName),
                    binding, "绑定用户已禁用");

            boundUserId = userDto.Id;
        }

        if (!string.IsNullOrWhiteSpace(binding.DeptCode))
        {
            var deptDto = await _deptQuery.GetByCodeAsync(binding.DeptCode, ct);
            if (deptDto is null)
                return RecorderIdentifyResult.DeptInactive(
                    MakeBound(boundUserId, boundDeptId, recorderDto.Id, binding.UserNo, binding.DeptCode, recorderDto.Code, binding.UserName, binding.DeptName),
                    binding, "绑定部门不在册");
            if (!deptDto.IsActive)
                return RecorderIdentifyResult.DeptInactive(
                    MakeBound(boundUserId, boundDeptId, recorderDto.Id, binding.UserNo, binding.DeptCode, recorderDto.Code, binding.UserName, binding.DeptName),
                    binding, "绑定部门已禁用");

            boundDeptId = deptDto.Id;
        }

        // ---- 全部通过 ----
        return RecorderIdentifyResult.Matched(
            MakeBound(boundUserId, boundDeptId, recorderDto.Id, binding.UserNo, binding.DeptCode, recorderDto.Code, binding.UserName, binding.DeptName),
            binding, recorderDto);
    }
}
