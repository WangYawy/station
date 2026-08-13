using System.Text;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.Digests;
using Station.Infrastructure.Security;

namespace Station.Infrastructure.Recorders;

/// <summary>
/// 记录仪根目录绑定文件（station_bind.ini）：记录仪编号/用户/部门/绑定时间 + SM3 签名。
/// 签名串 = serial|model|userNo|userName|deptCode|deptName|boundAt|secret。
/// </summary>
public sealed class RecorderBindingFile
{
    public const string FileName = "station_bind.ini";

    private readonly string _secret;
    private readonly IRecorderRootFileStore _rootStore;

    public RecorderBindingFile(
        IOptions<BindingOptions> options,
        IRecorderRootFileStore? rootStore = null)
    {
        _secret = Sm4SecretProtector.TryUnprotect(options.Value.Secret) ?? options.Value.Secret;
        _rootStore = rootStore ?? new FileSystemRecorderRootFileStore();
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
        _rootStore.WriteFile(recorderRootPath, FileName, content);
    }

    public BindingInfo? Read(string recorderRootPath)
    {
        var text = _rootStore.ReadFile(recorderRootPath, FileName);
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

        if (!values.TryGetValue("recorder_serial", out var serial) ||
            !values.TryGetValue("user_no", out var userNo) ||
            !values.TryGetValue("sm3", out var signature) ||
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

    public bool VerifySignature(BindingInfo binding) =>
        ComputeSignature(binding.RecorderSerial, binding.RecorderModel, binding.UserNo, binding.UserName, binding.DeptCode, binding.DeptName, binding.BoundAt)
            .Equals(binding.Signature, StringComparison.OrdinalIgnoreCase);

    public string ComputeSignature(string serial, string model, string userNo, string userName, string deptCode, string deptName, DateTime boundAt)
    {
        var canonical = string.Join('|', serial, model, userNo, userName, deptCode, deptName, boundAt.ToString("O"), _secret);
        return Sm3Hex(canonical);
    }

    public void Delete(string recorderRootPath)
    {
        _rootStore.DeleteFile(recorderRootPath, FileName);
    }

    private static string Sm3Hex(string text)
    {
        var digest = new SM3Digest();
        var bytes = Encoding.UTF8.GetBytes(text);
        digest.BlockUpdate(bytes, 0, bytes.Length);
        var output = new byte[digest.GetDigestSize()];
        digest.DoFinal(output, 0);
        return Convert.ToHexString(output).ToLowerInvariant();
    }
}
