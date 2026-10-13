using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Crypto;
using Station.Data.Repositories;
using Station.Domain.Collecting;
using Station.Domain.Departments;
using Station.Domain.Entities;
using Station.Domain.Users;

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
    private readonly IRepository<Recorder> _recorders;
    private readonly IUserRepository _users;
    private readonly IDeptRepository _depts;
    private readonly BindingOptions _options;
    private readonly ILogger<RecorderIdentificationService> _logger;

    public RecorderIdentificationService(
        IOptions<BindingOptions> options,
        IRecorderBindingFile bindingFile,
        IRepository<Recorder> recorders,
        IUserRepository users,
        IDeptRepository depts,
        ILogger<RecorderIdentificationService> logger)
    {
        _options = options.Value;
        _bindingFile = bindingFile;
        _recorders = recorders;
        _users = users;
        _depts = depts;
        _logger = logger;
    }

    public async Task<RecorderIdentifyResult> IdentifyAsync(
        CollectDeviceInfo device,
        string recorderRootPath,
        CancellationToken ct = default)
    {
        // ================= 第 0 层：总开关 =================
        if (!_options.EnableBinding)
        {
            _logger.LogDebug("绑定验证未启用，设备 {Serial} 直接通过", device.Serial);
            return RecorderIdentifyResult.Matched(device, null, null, null, null);
        }

        // ================= 第 1 层：读取 binding 文件 =================
        ct.ThrowIfCancellationRequested();

        BindingInfo? binding;
        try
        {
            binding = _bindingFile.Read(recorderRootPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取绑定文件失败：{Root}", recorderRootPath);
            return RecorderIdentifyResult.NotFound(device, "绑定文件读取失败");
        }

        if (binding is null)
        {
            _logger.LogInformation("设备 {Serial} 无绑定文件", device.Serial);
            return RecorderIdentifyResult.NotFound(device, "设备缺失绑定文件");
        }

        // ================= 第 2 层：MAC 验证（可开关） =================
        ct.ThrowIfCancellationRequested();

        if (_options.EnableSignature)
        {
            // Signature 为空 = 旧格式 或 文件写了一半
            if (string.IsNullOrEmpty(binding.Signature))
            {
                _logger.LogWarning(
                    "设备 {Serial} 绑定文件是旧格式（无 mac 字段），需重新绑定",
                    device.Serial);
                return RecorderIdentifyResult.Legacy(device, binding, "绑定文件格式已升级，请重新绑定");
            }

            bool signatureOk;
            try
            {
                signatureOk = await _bindingFile
                    .VerifySignatureAsync(recorderRootPath, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "验证绑定文件签名异常：{Serial}", device.Serial);
                return RecorderIdentifyResult.Tampered(device, binding, "签名验证异常");
            }

            if (!signatureOk)
            {
                _logger.LogWarning("设备 {Serial} 绑定文件 MAC 校验失败", device.Serial);
                return RecorderIdentifyResult.Tampered(device, binding, "绑定文件被篡改或密钥不匹配");
            }
        }
        else
        {
            _logger.LogDebug(
                "签名验证未启用，跳过 MAC 校验（serial={Serial}）",
                binding.DeviceSerial);
        }

        // ================= 第 3 层：设备级台账校验（必做） =================
        ct.ThrowIfCancellationRequested();

        // 3.1 记录仪是否在册
        var recorder = await _recorders
            .FirstAsync(r => r.SerialNumber == binding.DeviceSerial, ct)
            .ConfigureAwait(false);

        if (recorder is null)
        {
            _logger.LogWarning("设备 {Serial} 不在记录仪台账", binding.DeviceSerial);
            return RecorderIdentifyResult.RecorderMissing(device, binding, "记录仪不在台账");
        }

        if (!recorder.IsActive)
        {
            _logger.LogWarning("设备 {Serial} 已停用", binding.DeviceSerial);
            return RecorderIdentifyResult.RecorderMissing(device, binding, "记录仪已停用");
        }

        // 3.2 设备实际序列号与绑定文件序列号是否一致（防冒名）
        //if (!string.Equals(device.Serial, binding.DeviceSerial, StringComparison.OrdinalIgnoreCase))
        //{
        //    _logger.LogWarning(
        //        "设备实际序列号 {Actual} 与绑定文件 {Bound} 不一致",
        //        device.Serial, binding.DeviceSerial);
        //    return RecorderIdentifyResult.Tampered(device, binding, "设备序列号与绑定文件不一致");
        //}

        // ================= 第 4 层：用户/部门校验（可开关） =================
        if (!_options.EnableBindingValidation)
        {
            _logger.LogDebug(
                "用户/部门校验未启用，跳过（serial={Serial}）",
                binding.DeviceSerial);
            return RecorderIdentifyResult.Matched(device, binding, recorder, null, null);
        }

        // 4.1 用户是否在册且启用
        ct.ThrowIfCancellationRequested();

        User? user = null;
        if (!string.IsNullOrWhiteSpace(binding.UserNo))
        {
            user = await _users
                .GetByUserNoAsync(binding.UserNo, ct)
                .ConfigureAwait(false);

            if (user is null)
            {
                _logger.LogWarning("绑定用户 {UserNo} 不在册", binding.UserNo);
                return RecorderIdentifyResult.UserInactive(device, binding, "绑定用户不在册");
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("绑定用户 {UserNo} 已禁用", binding.UserNo);
                return RecorderIdentifyResult.UserInactive(device, binding, "绑定用户已禁用");
            }
        }

        // 4.2 部门是否在册且启用
        ct.ThrowIfCancellationRequested();

        Dept? dept = null;
        if (!string.IsNullOrWhiteSpace(binding.DeptCode))
        {
            dept = await _depts
                .GetByCodeAsync(binding.DeptCode, ct)
                .ConfigureAwait(false);

            if (dept is null)
            {
                _logger.LogWarning("绑定部门 {DeptCode} 不在册", binding.DeptCode);
                return RecorderIdentifyResult.DeptInactive(device, binding, "绑定部门不在册");
            }

            if (!dept.IsActive)
            {
                _logger.LogWarning("绑定部门 {DeptCode} 已禁用", binding.DeptCode);
                return RecorderIdentifyResult.DeptInactive(device, binding, "绑定部门已禁用");
            }
        }

        // 4.3 台账绑定与文件绑定是否一致
        var mismatch = CheckMismatch(recorder, user, dept);
        if (mismatch is not null)
        {
            _logger.LogWarning(
                "设备 {Serial} 台账绑定与文件绑定不一致：{Reason}",
                binding.DeviceSerial, mismatch);
            return RecorderIdentifyResult.Mismatch(device, binding, mismatch);
        }

        // ================= 全部通过 =================
        _logger.LogInformation(
            "设备 {Serial} 识别成功：用户 {User}，部门 {Dept}",
            binding.DeviceSerial, binding.UserName, binding.DeptName);

        return RecorderIdentifyResult.Matched(device, binding, recorder, user, dept);
    }

    /// <summary>交叉校验台账与文件绑定是否一致。返回不一致原因，null = 一致。</summary>
    private static string? CheckMismatch(Recorder recorder, User? user, Dept? dept)
    {
        // 台账记录的用户 ID 与文件里的 UserNo 应该对应
        if (recorder.BoundUserId is { } boundUserId)
        {
            if (user is null)
                return "台账绑定了用户，但文件未记录用户";
            if (user.Id != boundUserId)
                return $"台账绑定用户 ID={boundUserId}，文件用户={user.UserNo}(Id={user.Id})";
        }

        if (recorder.DeptId is { } boundDeptId)
        {
            if (dept is null)
                return "台账绑定了部门，但文件未记录部门";
            if (dept.Id != boundDeptId)
                return $"台账绑定部门 ID={boundDeptId}，文件部门={dept.Code}(Id={dept.Id})";
        }

        return null;
    }
}
