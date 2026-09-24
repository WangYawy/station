using Microsoft.Extensions.Logging;
using Station.Domain.Collecting;
using Station.Domain.Entities;
using Station.Domain.Repositories;

namespace Station.Application.Recorders;

/// <summary>
/// 记录仪识别服务实现。
/// 
/// 【与新 RecorderBindingFile 的配合】
///   - Read 仍同步（读 ini 文件）；
///   - VerifySignatureAsync 异步（内部读策略 + 算 MAC）；
///   - 新 Read 只读 "mac=" 字段；旧文件的 "sm3=" 不会被读入 → Signature 为空。
/// 
/// 【识别结果分类】
///   1) NotFound      无 station_bind.ini
///   2) Tampered      文件存在但 MAC 验证失败（被篡改）
///   3) Legacy        文件是旧格式（Signature 为空但文件有内容）
///   4) RecorderMissing   MAC 有效但记录仪不在台账
///   5) UserInactive      MAC 有效但绑定用户已禁用
///   6) DeptInactive      MAC 有效但绑定部门已禁用
///   7) Matched          全部校验通过
///   8) Mismatch         MAC 有效但与台账的绑定不一致
/// </summary>
public sealed class RecorderIdentificationService : IRecorderIdentificationService
{
    private readonly RecorderBindingFile _bindingFile;
    private readonly IRepository<Recorder> _recorders;
    private readonly IRepository<User> _users;
    private readonly IRepository<Dept> _depts;
    private readonly ILogger<RecorderIdentificationService> _logger;

    public RecorderIdentificationService(
        RecorderBindingFile bindingFile,
        IRepository<Recorder> recorders,
        IRepository<User> users,
        IRepository<Dept> depts,
        ILogger<RecorderIdentificationService> logger)
    {
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
            return RecorderIdentifyResult.NotFound(device, "设备未绑定");
        }

        // ================= 第 2 层：MAC 验证 =================
        ct.ThrowIfCancellationRequested();

        // 判断是否为旧格式（新 Read 只读 mac=，旧文件 sm3= 不会被读入）
        // Signature 为空 = 旧格式 或 文件写了一半
        if (string.IsNullOrEmpty(binding.Signature))
        {
            _logger.LogWarning("设备 {Serial} 绑定文件是旧格式（无 mac 字段），需重新绑定",
                device.Serial);
            return RecorderIdentifyResult.Legacy(device, binding, "绑定文件格式已升级，请重新绑定");
        }

        bool signatureOk;
        try
        {
            signatureOk = await _bindingFile.VerifySignatureAsync(binding, ct).ConfigureAwait(false);
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

        // ================= 第 3 层：台账一致性 =================
        ct.ThrowIfCancellationRequested();

        // 3.1 记录仪是否在册
        var recorder = await _recorders
            .FirstAsync(r => r.SerialNumber == binding.RecorderSerial, ct)
            .ConfigureAwait(false);

        if (recorder is null)
        {
            _logger.LogWarning("设备 {Serial} 不在记录仪台账", binding.RecorderSerial);
            return RecorderIdentifyResult.RecorderMissing(device, binding, "记录仪不在台账");
        }

        if (!recorder.IsActive)
        {
            _logger.LogWarning("设备 {Serial} 已停用", binding.RecorderSerial);
            return RecorderIdentifyResult.RecorderMissing(device, binding, "记录仪已停用");
        }

        // 3.2 序列号与设备实际序列号是否一致（防冒名）
        if (!string.Equals(device.Serial, binding.RecorderSerial, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "设备实际序列号 {Actual} 与绑定文件 {Bound} 不一致",
                device.Serial, binding.RecorderSerial);
            return RecorderIdentifyResult.Tampered(device, binding, "设备序列号与绑定文件不一致");
        }

        // 3.3 用户是否在册且启用
        ct.ThrowIfCancellationRequested();

        User? user = null;
        if (!string.IsNullOrWhiteSpace(binding.UserNo))
        {
            user = await _users
                .FirstAsync(u => u.UserNo == binding.UserNo, ct)
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

        // 3.4 部门是否在册且启用
        ct.ThrowIfCancellationRequested();

        Dept? dept = null;
        if (!string.IsNullOrWhiteSpace(binding.DeptCode))
        {
            dept = await _depts
                .FirstAsync(d => d.Code == binding.DeptCode, ct)
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

        // 3.5 台账绑定与文件绑定是否一致
        // （可选：如果台账记录了 BoundUserId / DeptId，可以交叉验证）
        var mismatch = CheckMismatch(recorder, user, dept);
        if (mismatch is not null)
        {
            _logger.LogWarning("设备 {Serial} 台账绑定与文件绑定不一致：{Reason}",
                binding.RecorderSerial, mismatch);
            return RecorderIdentifyResult.Mismatch(device, binding, mismatch);
        }

        // ================= 全部通过 =================
        _logger.LogInformation("设备 {Serial} 识别成功：用户 {User}，部门 {Dept}",
            binding.RecorderSerial, binding.UserName, binding.DeptName);

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
