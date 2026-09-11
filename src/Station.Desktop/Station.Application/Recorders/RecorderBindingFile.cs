using Microsoft.Extensions.Options;
using Station.Application.Collecting;
using Station.Contracts;
using Station.Domain.Collecting;
using Station.Domain.Security;

namespace Station.Application.Recorders;

/// <summary>
/// 记录仪根目录绑定文件（station_bind.ini）：记录仪编号/用户/部门/绑定时间 + SM3 签名。
/// 签名串 = serial|model|userNo|userName|deptCode|deptName|boundAt|secret。
/// </summary>
public sealed class RecorderBindingFile
{
    public const string FileName = "station_bind.ini";

    private readonly string _secret;
    private readonly ICollectSourceProvider _sourceProvider;
    private readonly ISecretProtector _secretProtector;
    private readonly IHashService _hashService;
    private readonly BindingOptions _options;

    public RecorderBindingFile(
        IOptions<BindingOptions> options,
        ISecretProtector secretProtector,
        IHashService hashService,
        ICollectSourceProvider sourceProvider)
    {
        _secretProtector = secretProtector;
        _secret = _secretProtector.TryUnprotect(options.Value.Secret) ?? options.Value.Secret;
        _options = options.Value;
        _hashService = hashService;
        _sourceProvider = sourceProvider;
    }

    public void Write(string recorderRootPath, BindingInfo binding)
    {
        var content = string.Join(Environment.NewLine,
            "[binding]",
            $"recorder_serial={binding.RecorderSerial}",
            $"recorder_model={binding.RecorderModel}",
            $"user_no={binding.UserNo}",
            $"user_name={binding.UserName}",
            $"dept_code={binding.DeptCode}",
            $"dept_name={binding.DeptName}",
            $"bound_at={binding.BoundAt:O}",
            $"sm3={binding.Signature}");

        var store = GetFileStore(recorderRootPath);
        store.WriteFile(recorderRootPath, FileName, content);
    }

    public BindingInfo? Read(string recorderRootPath)
    {
        var store = GetFileStore(recorderRootPath);
        var text = store.ReadFile(recorderRootPath, FileName);
        if (text is null)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') || trimmed.StartsWith('#') || !trimmed.Contains('='))
            {
                continue;
            }

            var index = trimmed.IndexOf('=');
            values[trimmed[..index].Trim()] = trimmed[(index + 1)..].Trim();
        }

        string signature = string.Empty;
        if (!values.TryGetValue("recorder_serial", out var serial) ||
            !values.TryGetValue("user_no", out var userNo) ||
            //!values.TryGetValue("sm3", out var signature) ||
            !DateTime.TryParse(values.GetValueOrDefault("bound_at"), out var boundAt))
        {
            return null;
        }

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

    public bool VerifySignature(BindingInfo binding)
    {
        if (_options.EnableSecret)
            return ComputeSignature(binding.RecorderSerial, binding.RecorderModel, binding.UserNo, binding.UserName, binding.DeptCode, binding.DeptName, binding.BoundAt)
                    .Equals(binding.Signature, StringComparison.OrdinalIgnoreCase);
        else return true;
    }


    public string ComputeSignature(string serial, string model, string userNo, string userName, string deptCode, string deptName, DateTime boundAt)
    {
        var canonical = string.Join('|', serial, model, userNo, userName, deptCode, deptName, boundAt.ToString("O"), _secret);
        return _hashService.ComputeHash(canonical);
    }

    public void Delete(string recorderRootPath)
    {
        var store = GetFileStore(recorderRootPath);
        store.DeleteFile(recorderRootPath, FileName);
    }

    // 根据根路径解析协议类型（辅助方法）
    private static ProtocolType ResolveProtocol(string recorderRoot)
    {
        if (MtpRoot.IsMtpRoot(recorderRoot))
            return ProtocolType.Mtp;
        // 默认 UMS（模拟源也走 UMS 的文件系统逻辑）
        return ProtocolType.Ums;
    }

    // 获取对应的文件存储适配器
    private IRecorderFileStore GetFileStore(string recorderRoot)
    {
        var protocol = ResolveProtocol(recorderRoot);
        // 关键：ICollectSource 继承了 IRecorderFileStore，所以可以强转
        var source = _sourceProvider.GetFor(protocol);

        // 防御性检查：理论上所有 CollectSource 都实现了 IRecorderFileStore
        if (source is not IRecorderFileStore store)
        {
            throw new InvalidOperationException(
                $"采集源 {source.GetType().Name} 未实现 IRecorderFileStore，无法读写绑定文件");
        }
        return store;
    }
}
