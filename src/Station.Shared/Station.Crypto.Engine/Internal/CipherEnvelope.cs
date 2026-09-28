namespace Station.Crypto.Engine.Internal;

/// <summary>
/// 密文信封工具（v{n}:base64 格式）。
///
/// 契约：所有 Encryptor 输出的密文均采用此格式：
///   v{版本号}:Base64(nonce || ciphertext || tag)
///
/// ⚠️ 该格式是持久化契约，一旦上线禁止修改。
/// </summary>
internal static class CipherEnvelope
{
    /// <summary>封装密文。</summary>
    public static string Wrap(int version, byte[] packed)
        => $"v{version}:{Convert.ToBase64String(packed)}";

    /// <summary>
    /// 解析密文，返回版本号与原始字节。
    /// </summary>
    /// <exception cref="CipherFormatException">格式错误。</exception>
    public static (int Version, byte[] Raw) Parse(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            throw new CipherFormatException("密文格式错误：内容为空");

        if (ciphertext[0] != 'v')
            throw new CipherFormatException("密文格式错误：缺少版本前缀");

        var idx = ciphertext.IndexOf(':');
        if (idx < 2)
            throw new CipherFormatException("密文格式错误：版本分隔符缺失");

        if (!int.TryParse(ciphertext.AsSpan(1, idx - 1), out var version))
            throw new CipherFormatException("密文格式错误：版本号非数字");

        try
        {
            var raw = Convert.FromBase64String(ciphertext[(idx + 1)..]);
            return (version, raw);
        }
        catch (FormatException ex)
        {
            throw new CipherFormatException("密文格式错误：Base64 解析失败", ex);
        }
    }
}
