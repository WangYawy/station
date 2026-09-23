using System.Text;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.Agreement.Kdf;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Station.Application.Recorders;
using Station.Application.Security.Abstractions;

namespace Station.Infrastructure.Security.Keys;

/// <summary>
/// Binding 密钥提供者实现。
/// 
/// 【派生】
///   主密钥（IMasterKeyProvider）→ HKDF-SM3(info="station:recorder-binding:v1")
///   → 32 字节 Binding 密钥。
/// 
/// 【兼容】
///   BindingOptions.Secret 若非空且非默认占位值，优先使用（老部署兼容）。
///   生产环境推荐留空，走主密钥派生。
/// </summary>
public sealed class BindingSecretProvider : IBindingSecretProvider
{
    private static readonly byte[] HkdfInfo =
        Encoding.UTF8.GetBytes("station:recorder-binding:v1");

    private const string DefaultPlaceholder = "station-dev-binding-secret";

    private readonly IMasterKeyProvider _masterKey;
    private readonly BindingOptions _options;

    public BindingSecretProvider(
        IMasterKeyProvider masterKey,
        IOptions<BindingOptions> options)
    {
        _masterKey = masterKey;
        _options = options.Value;
    }

    public int GetKeyVersion() => _masterKey.CurrentVersion;

    public byte[] GetSecret()
    {
        // 兼容：显式配置（非占位值）优先
        var configured = _options.Secret;
        if (!string.IsNullOrWhiteSpace(configured)
            && !string.Equals(configured, DefaultPlaceholder, StringComparison.Ordinal))
        {
            return Encoding.UTF8.GetBytes(configured);
        }

        // 主密钥 HKDF 派生
        var masterKey = _masterKey.GetKey(_masterKey.CurrentVersion);
        var hkdf = new HkdfBytesGenerator(new SM3Digest());
        hkdf.Init(new HkdfParameters(masterKey, null, HkdfInfo));
        var derived = new byte[32];
        hkdf.GenerateBytes(derived, 0, 32);
        return derived;
    }
}
