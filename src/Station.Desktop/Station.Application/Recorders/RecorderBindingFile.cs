using System.Text;
using Microsoft.Extensions.Options;
using Station.Application.Collecting;
using Station.Application.Security.Abstractions;
using Station.Contracts;
using Station.Domain.Collecting;
using Station.Domain.Security;

namespace Station.Application.Recorders;

/// <summary>
/// 记录仪根目录绑定文件（station_bind.ini）：记录仪编号/用户/部门/绑定时间 + MAC。
/// 
/// 【签名串】serial|model|userNo|userName|deptCode|deptName|boundAt
/// 【MAC 算法】由 CryptoUsage.RecorderBinding 策略决定（默认 HMAC-SM3）
/// 【密钥来源】IBindingSecretProvider（主密钥 HKDF 派生）
/// </summary>
public sealed class RecorderBindingFile
{
    public const string FileName = "station_bind.ini";
    private const string MacField = "mac";
    private const string MacAlgoField = "mac_algo";

    private readonly ICollectSourceProvider _sourceProvider;
    private readonly ICryptoPolicyService _cryptoPolicy;
    private readonly ICryptoProviderFactory _cryptoFactory;
    private readonly IBindingSecretProvider _secretProvider;
    private readonly BindingOptions _options;

    public RecorderBindingFile(
        IOptions<BindingOptions> options,
        ICollectSourceProvider sourceProvider,
        ICryptoPolicyService cryptoPolicy,
        ICryptoProviderFactory cryptoFactory,
        IBindingSecretProvider secretProvider)
    {
        _options = options.Value;
        _sourceProvider = sourceProvider;
        _cryptoPolicy = cryptoPolicy;
        _cryptoFactory = cryptoFactory;
        _secretProvider = secretProvider;
    }

    // =========================================================
    // 写入
    // =========================================================

    public async Task WriteAsync(
        string recorderRootPath, BindingInfo binding, CancellationToken ct = default)
    {
        var (mac, macAlgo) = await ComputeMacAsync(binding, ct).ConfigureAwait(false);

        var content = string.Join(Environment.NewLine,
            "[binding]",
            $"recorder_serial={binding.RecorderSerial}",
            $"recorder_model={binding.RecorderModel}",
            $"user_no={binding.UserNo}",
            $"user_name={binding.UserName}",
            $"dept_code={binding.DeptCode}",
            $"dept_name={binding.DeptName}",
            $"bound_at={binding.BoundAt:O}",
            $"{MacAlgoField}={macAlgo}",
            $"{MacField}={mac}");

        var store = GetFileStore(recorderRootPath);
        store.WriteFile(recorderRootPath, FileName, content);
    }

    // =========================================================
    // 读取（不需要算 MAC，同步）
    // =========================================================

    public BindingInfo? Read(string recorderRootPath)
    {
        var store = GetFileStore(recorderRootPath);
        var text = store.ReadFile(recorderRootPath, FileName);
        if (text is null) return null;

        var values = ParseIni(text);

        if (!values.TryGetValue("recorder_serial", out var serial)
            || !values.TryGetValue("user_no", out var userNo)
            || !DateTime.TryParse(values.GetValueOrDefault("bound_at"), out var boundAt))
        {
            return null;
        }

        // 读取时取 mac 字段（旧 sm3 字段不再兼容）
        var signature = values.GetValueOrDefault(MacField) ?? string.Empty;

        return new BindingInfo(
            serial,
            values.GetValueOrDefault("recorder_model", string.Empty),
            userNo,
            values.GetValueOrDefault("user_name", string.Empty),
            values.GetValueOrDefault("dept_code", string.Empty),
            values.GetValueOrDefault("dept_name", string.Empty),
            boundAt,
            signature);
    }

    // =========================================================
    // 验证
    // =========================================================

    public async Task<bool> VerifySignatureAsync(
        BindingInfo binding, CancellationToken ct = default)
    {
        if (!_options.EnableSecret) return true;

        var (expected, _) = await ComputeMacAsync(binding, ct).ConfigureAwait(false);
        return string.Equals(expected, binding.Signature, StringComparison.Ordinal);
    }

    // =========================================================
    // 内部
    // =========================================================

    private async Task<(string Mac, string Algorithm)> ComputeMacAsync(
        BindingInfo binding, CancellationToken ct)
    {
        // 1) 读策略（默认 HMAC-SM3）
        var policy = await _cryptoPolicy
            .GetAsync(CryptoUsage.RecorderBinding, ct)
            .ConfigureAwait(false);

        // 2) 取 MAC 实现
        var mac = _cryptoFactory.GetMacProvider(policy.Algorithm);

        // 3) 取密钥（IBindingSecretProvider 内部走主密钥 HKDF）
        var key = _secretProvider.GetSecret();

        // 4) 规范化签名串（不含密钥）
        var canonical = string.Join('|',
            binding.RecorderSerial,
            binding.RecorderModel,
            binding.UserNo,
            binding.UserName,
            binding.DeptCode,
            binding.DeptName,
            binding.BoundAt.ToString("O"));

        // 5) 计算 MAC
        var value = mac.Compute(key, Encoding.UTF8.GetBytes(canonical));
        return (value, policy.Algorithm);
    }

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

    public void Delete(string recorderRootPath)
    {
        var store = GetFileStore(recorderRootPath);
        store.DeleteFile(recorderRootPath, FileName);
    }

    // ---- 协议路由 ----

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
