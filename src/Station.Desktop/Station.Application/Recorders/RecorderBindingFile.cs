using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Collecting;
using Station.Application.Security;
using Station.Contracts;
using Station.Domain.Collecting;
using Station.Crypto;

namespace Station.Application.Recorders;

/// <summary>
/// 记录仪根目录绑定文件（station_bind.ini）：记录仪编号/用户/部门/绑定时间 + MAC。
/// 
/// 【签名串】serial|model|userNo|userName|deptCode|deptName|boundAt
/// 【MAC 算法】由 CryptoUsage.RecorderBinding 策略决定（默认 HMAC-SM3）
/// 【密钥来源】从主密钥 HKDF 派生，通过 ICryptoPolicyService.GetBindingMacKeyAsync 获取
/// 
/// 【INI 格式】
///   [binding]
///   recorder_serial=...
///   recorder_model=...
///   user_no=...
///   user_name=...
///   dept_code=...
///   dept_name=...
///   bound_at=2026-09-24T10:30:00.0000000+08:00
///   mac_algo=HMAC-SM3
///   mac=base64(...)
/// 
/// 【依赖倒置】
///   本类只依赖 Application 层接口（ICryptoPolicyService / ICollectSourceProvider），
///   不引用 Infrastructure 层的任何类型。
/// </summary>
public sealed class RecorderBindingFile
{
    public const string FileName = "station_bind.ini";

    private const string FieldSerial = "recorder_serial";
    private const string FieldModel = "recorder_model";
    private const string FieldUserNo = "user_no";
    private const string FieldUserName = "user_name";
    private const string FieldDeptCode = "dept_code";
    private const string FieldDeptName = "dept_name";
    private const string FieldBoundAt = "bound_at";
    private const string FieldMacAlgo = "mac_algo";
    private const string FieldMac = "mac";

    private readonly ICollectSourceProvider _sourceProvider;
    private readonly ICryptoPolicyService _cryptoPolicy;
    private readonly BindingOptions _options;
    private readonly ILogger<RecorderBindingFile> _logger;

    public RecorderBindingFile(
        IOptions<BindingOptions> options,
        ICollectSourceProvider sourceProvider,
        ICryptoPolicyService cryptoPolicy,
        ILogger<RecorderBindingFile> logger)
    {
        _options = options.Value;
        _sourceProvider = sourceProvider;
        _cryptoPolicy = cryptoPolicy;
        _logger = logger;
    }

    // =========================================================
    // 写入
    // =========================================================

    public async Task WriteAsync(
        string recorderRootPath, BindingInfo binding, CancellationToken ct = default)
    {
        // 内部自动算 MAC（调用方不需要手动算）
        var (mac, macAlgo) = await ComputeMacAsync(binding, ct).ConfigureAwait(false);

        var content = string.Join(Environment.NewLine,
            "[binding]",
            $"{FieldSerial}={binding.RecorderSerial}",
            $"{FieldModel}={binding.RecorderModel}",
            $"{FieldUserNo}={binding.UserNo}",
            $"{FieldUserName}={binding.UserName}",
            $"{FieldDeptCode}={binding.DeptCode}",
            $"{FieldDeptName}={binding.DeptName}",
            $"{FieldBoundAt}={binding.BoundAt:O}",
            $"{FieldMacAlgo}={macAlgo}",
            $"{FieldMac}={mac}");

        var store = GetFileStore(recorderRootPath);
        store.WriteFile(recorderRootPath, FileName, content);

        _logger.LogDebug("绑定文件已写入：{Path}（MAC 算法 {Algo}）",
            recorderRootPath, macAlgo);
    }

    // =========================================================
    // 读取（同步，不需要算 MAC）
    // =========================================================

    public BindingInfo? Read(string recorderRootPath)
    {
        var store = GetFileStore(recorderRootPath);
        var text = store.ReadFile(recorderRootPath, FileName);
        if (text is null) return null;

        var values = ParseIni(text);

        if (!values.TryGetValue(FieldSerial, out var serial)
            || !values.TryGetValue(FieldUserNo, out var userNo)
            || !DateTime.TryParse(values.GetValueOrDefault(FieldBoundAt), out var boundAt))
        {
            _logger.LogWarning("绑定文件格式错误：{Path}", recorderRootPath);
            return null;
        }

        // 只读 mac 字段（旧 sm3 字段不再兼容）
        var mac = values.GetValueOrDefault(FieldMac) ?? string.Empty;

        return new BindingInfo(
            RecorderSerial: serial,
            RecorderModel: values.GetValueOrDefault(FieldModel, string.Empty),
            UserNo: userNo,
            UserName: values.GetValueOrDefault(FieldUserName, string.Empty),
            DeptCode: values.GetValueOrDefault(FieldDeptCode, string.Empty),
            DeptName: values.GetValueOrDefault(FieldDeptName, string.Empty),
            BoundAt: boundAt,
            Signature: mac);
    }

    // =========================================================
    // 验证
    // =========================================================

    /// <summary>
    /// 验证 MAC。返回 true/false；异常情况（策略缺失等）会向上抛。
    /// </summary>
    public async Task<bool> VerifySignatureAsync(
        BindingInfo binding, CancellationToken ct = default)
    {
        if (!_options.EnableSecret) return true;

        var (expected, _) = await ComputeMacAsync(binding, ct).ConfigureAwait(false);

        // 固定时间比较，防时序攻击
        var match = string.Equals(expected, binding.Signature, StringComparison.Ordinal);
        if (!match)
            _logger.LogWarning("绑定文件 MAC 校验失败（serial={Serial}）", binding.RecorderSerial);

        return match;
    }

    // =========================================================
    // 删除
    // =========================================================

    public void Delete(string recorderRootPath)
    {
        var store = GetFileStore(recorderRootPath);
        store.DeleteFile(recorderRootPath, FileName);
    }

    // =========================================================
    // 内部：MAC 计算
    // =========================================================

    private async Task<(string Mac, string Algorithm)> ComputeMacAsync(
        BindingInfo binding, CancellationToken ct)
    {
        // 1) 读策略（默认 HMAC-SM3），取 MAC 提供者
        var mac = await _cryptoPolicy
            .GetMacProviderAsync(CryptoUsage.RecorderBinding, ct)
            .ConfigureAwait(false);

        // 2) 取密钥（从主密钥 HKDF 派生，内部 info="station:recorder-binding:v1"）
        var key = await _cryptoPolicy
            .GetBindingMacKeyAsync(ct)
            .ConfigureAwait(false);

        // 3) 规范化签名串（不含密钥；字段用 | 分隔）
        var canonical = string.Join('|',
            binding.RecorderSerial,
            binding.RecorderModel,
            binding.UserNo,
            binding.UserName,
            binding.DeptCode,
            binding.DeptName,
            binding.BoundAt.ToString("O"));

        // 4) 计算 MAC
        var value = mac.Compute(key, Encoding.UTF8.GetBytes(canonical));
        return (value, mac.Algorithm);
    }

    // =========================================================
    // 内部：INI 解析与协议路由
    // =========================================================

    private static Dictionary<string, string> ParseIni(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') || trimmed.StartsWith('#') || !trimmed.Contains('='))
                continue;

            var idx = trimmed.IndexOf('=');
            values[trimmed[..idx].Trim()] = trimmed[(idx + 1)..].Trim();
        }
        return values;
    }

    private static ProtocolType ResolveProtocol(string recorderRoot)
    {
        if (MtpRoot.IsMtpRoot(recorderRoot)) return ProtocolType.Mtp;
        return ProtocolType.Ums;
    }

    private IRecorderFileStore GetFileStore(string recorderRoot)
    {
        var protocol = ResolveProtocol(recorderRoot);
        var source = _sourceProvider.GetFor(protocol);

        if (source is not IRecorderFileStore store)
            throw new InvalidOperationException(
                $"采集源 {source.GetType().Name} 未实现 IRecorderFileStore，无法读写绑定文件");
        return store;
    }
}
