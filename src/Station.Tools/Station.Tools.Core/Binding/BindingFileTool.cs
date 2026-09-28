using Station.Crypto;
using Station.Crypto.Engine.Binding;
using Station.Crypto.Engine.Internal;
using Station.Crypto.Engine.Kdf;
using Station.Crypto.Engine.Keys;
using Station.Crypto.Engine.Models;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Binding;

/// <summary>
/// 记录仪绑定文件工具（薄壳，核心逻辑在 <see cref="BindingEngine"/>）。
/// </summary>
public static class BindingFileTool
{
    /// <summary>生成绑定文件内容。</summary>
    public static ToolResult<string> Generate(
        BindingInfo info,
        MasterKeyFileDto masterKeyFile,
        string algorithm = CryptoAlgorithm.HmacSm3)
    {
        try
        {
            var bindingKey = DeriveBindingKey(masterKeyFile);
            var macProvider = AlgorithmRegistry.Default.GetMacProvider(algorithm);
            var content = BindingEngine.Build(info, bindingKey, macProvider);
            return ToolResult<string>.Ok(content);
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("BINDING_GEN_FAIL", ex.Message);
        }
    }

    /// <summary>验证绑定文件内容。</summary>
    public static ToolResult<bool> Validate(string content, MasterKeyFileDto masterKeyFile)
    {
        try
        {
            var bindingKey = DeriveBindingKey(masterKeyFile);
            var ok = BindingEngine.Validate(
                content, bindingKey, AlgorithmRegistry.Default.GetMacProvider);
            return ToolResult<bool>.Ok(ok);
        }
        catch (Exception ex)
        {
            return ToolResult<bool>.Fail("BINDING_VERIFY_FAIL", ex.Message);
        }
    }

    /// <summary>查看绑定信息。</summary>
    public static ToolResult<BindingInfo> Inspect(string content)
    {
        try
        {
            var info = BindingEngine.Inspect(content);
            return info is null
                ? ToolResult<BindingInfo>.Fail("PARSE_FAIL", "绑定文件格式错误")
                : ToolResult<BindingInfo>.Ok(info);
        }
        catch (Exception ex)
        {
            return ToolResult<BindingInfo>.Fail("INSPECT_FAIL", ex.Message);
        }
    }

    /// <summary>
    /// 从主密钥文件派生 Binding MAC 密钥。
    /// 约定：HKDF-SM3(主密钥, HkdfInfo.RecorderBinding, 32)。
    /// </summary>
    private static byte[] DeriveBindingKey(MasterKeyFileDto keyFile)
    {
        ArgumentNullException.ThrowIfNull(keyFile);
        var master = keyFile.GetKey(keyFile.Current);
        return HkdfSm3.Derive(master, HkdfInfo.RecorderBinding, 32);
    }
}
