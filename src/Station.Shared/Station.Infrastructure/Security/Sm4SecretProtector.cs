using System.Text;

namespace Station.Infrastructure.Security;

/// <summary>SM4 凭据/敏感信息保护器：`sm4:` 前缀密文（base64(IV+密文)）；无前缀原样返回（兼容未升级配置）。</summary>
public static class Sm4SecretProtector
{
    public const string Prefix = "sm4:";

    public static string Protect(string value) =>
        Prefix + Convert.ToBase64String(
            Sm4Crypto.EncryptCbc(Sm4KeyProvider.Default.GetKey(), Encoding.UTF8.GetBytes(value)));

    public static string? TryUnprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return value;
        }

        var blob = Convert.FromBase64String(value[Prefix.Length..]);
        return Encoding.UTF8.GetString(Sm4Crypto.DecryptCbc(Sm4KeyProvider.Default.GetKey(), blob));
    }
}
