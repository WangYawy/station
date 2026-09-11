
namespace Station.Domain.Security;
/// <summary>
/// 许可证签名服务
/// </summary>
public interface ILicenseSignatureService
{
    /// <summary>
    /// 私钥签名
    /// </summary>
    /// <param name="privateKeyPem"></param>
    /// <param name="text"></param>
    /// <returns></returns>
    string Sign(string privateKeyPem, string text);
    /// <summary>
    /// 公钥验证
    /// </summary>
    /// <param name="publicKeyPem"></param>
    /// <param name="text"></param>
    /// <param name="signature"></param>
    /// <returns></returns>
    bool Verify(string publicKeyPem, string text, string signature);
    /// <summary>
    /// 派生公钥
    /// </summary>
    /// <param name="privateKeyPem"></param>
    /// <returns></returns>
    string DerivePublicKey(string privateKeyPem);
}
