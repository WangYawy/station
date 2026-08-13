namespace Station.Application.Licensing;

/// <summary>
/// 授权生成工具（内部工具/测试用）：输入 站点编号+硬件指纹+到期时间，产出签名授权文件。
/// 正式交付由内部独立工具持有签名密钥生成。
/// </summary>
public sealed class LicenseGenerator
{
    private readonly LicenseOptions _options;

    public LicenseGenerator(LicenseOptions options)
    {
        _options = options;
    }

    public LicenseFile Generate(string stationCode, string fingerprint, DateTime expiresAt)
    {
        if (string.IsNullOrWhiteSpace(_options.PrivateKeyPem))
        {
            throw new InvalidOperationException("未配置授权签名私钥（内部工具）");
        }

        var file = new LicenseFile(
            $"LIC-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            _options.ProductCode,
            stationCode,
            fingerprint,
            DateTime.Now,
            expiresAt,
            string.Empty);
        return file with { Signature = LicenseFileCodec.Sign(file, _options.PrivateKeyPem) };
    }

    public string GenerateFileText(string stationCode, string fingerprint, DateTime expiresAt) =>
        LicenseFileCodec.Serialize(Generate(stationCode, fingerprint, expiresAt));
}
