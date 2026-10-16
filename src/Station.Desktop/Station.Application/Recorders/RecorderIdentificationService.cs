using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Departments;
using Station.Application.Users;
using Station.Crypto;
using Station.Domain.Collecting;

namespace Station.Application.Recorders;

/// <summary>
/// 记录仪识别服务实现。
///
/// 【四层校验流程】
///   0) 总开关             EnableBinding=false → 直接 Matched（不读文件）
///   1) 绑定文件存在性     无 station_bind.ini → NotFound
///   2) MAC 验证            EnableSignature=true 时才执行 → Legacy / Tampered
///   3) 设备级台账校验      始终执行 → RecorderMissing / Tampered
///   4) 用户/部门校验       EnableUserDeptValidation=true 时才执行 → UserInactive / DeptInactive / Mismatch
///
/// 【识别结果分类】
///   1) NotFound          无 station_bind.ini
///   2) Tampered          文件存在但 MAC 验证失败（被篡改）或序列号不匹配
///   3) Legacy            文件是旧格式（Signature 为空但文件有内容）
///   4) RecorderMissing   MAC 有效但记录仪不在台账 / 已停用
///   5) UserInactive      MAC 有效但绑定用户不在册 / 已禁用
///   6) DeptInactive      MAC 有效但绑定部门不在册 / 已禁用
///   7) Mismatch          MAC 有效但与台账的绑定不一致
///   8) Matched           全部校验通过
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
        CollectDeviceInfo device, string recorderRootPath, CancellationToken ct = default)
    {
        // ---- 第 0 层：总开关 ----
        if (!_options.EnableBinding)
            return RecorderIdentifyResult.Matched(device, null, null);

        // ---- 第 1 层：读 binding 文件 ----
        ct.ThrowIfCancellationRequested();
        BindingInfo? binding;
        try { binding = _bindingFile.Read(recorderRootPath); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取绑定文件失败：{Root}", recorderRootPath);
            return RecorderIdentifyResult.NotFound(device, "绑定文件读取失败");
        }

        if (binding is null)
            return RecorderIdentifyResult.NotFound(device, "设备缺失绑定文件");

        // ---- 第 2 层：MAC 验证 ----
        ct.ThrowIfCancellationRequested();
        if (_options.EnableSignature)
        {
            if (string.IsNullOrEmpty(binding.Signature))
                return RecorderIdentifyResult.Legacy(device, binding, "绑定文件格式已升级，请重新绑定");

            bool signatureOk;
            try
            {
                signatureOk = await _bindingFile.VerifySignatureAsync(recorderRootPath, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "验证绑定文件签名异常：{Serial}", device.Serial);
                return RecorderIdentifyResult.Tampered(device, binding, "签名验证异常");
            }

            if (!signatureOk)
                return RecorderIdentifyResult.Tampered(device, binding, "绑定文件被篡改或密钥不匹配");
        }

        // ---- 第 3 层：台账校验（读侧 QueryService） ----
        ct.ThrowIfCancellationRequested();
        var recorderDto = await _recorderQuery.GetBySerialAsync(binding.DeviceSerial, ct);
        if (recorderDto is null)
            return RecorderIdentifyResult.RecorderMissing(device, binding, "记录仪不在台账");
        if (!recorderDto.IsActive)
            return RecorderIdentifyResult.RecorderMissing(device, binding, "记录仪已停用");

        // ---- 第 4 层：用户/部门校验 ----
        if (!_options.EnableBindingValidation)
            return RecorderIdentifyResult.Matched(device, binding, recorderDto);

        ct.ThrowIfCancellationRequested();

        // 用户
        if (!string.IsNullOrWhiteSpace(binding.UserNo))
        {
            var userDto = await _userQuery.GetByUserNoAsync(binding.UserNo, ct);
            if (userDto is null)
                return RecorderIdentifyResult.UserInactive(device, binding, "绑定用户不在册");
            if (!userDto.IsActive)
                return RecorderIdentifyResult.UserInactive(device, binding, "绑定用户已禁用");
        }

        // 部门
        if (!string.IsNullOrWhiteSpace(binding.DeptCode))
        {
            var deptDto = await _deptQuery.GetByCodeAsync(binding.DeptCode, ct);
            if (deptDto is null)
                return RecorderIdentifyResult.DeptInactive(device, binding, "绑定部门不在册");
            if (!deptDto.IsActive)
                return RecorderIdentifyResult.DeptInactive(device, binding, "绑定部门已禁用");
        }

        // 返回 Matched（识别服务不再返回 User / Dept 实体，由调用方按需查询）
        return RecorderIdentifyResult.Matched(device, binding, recorderDto);
    }
}
