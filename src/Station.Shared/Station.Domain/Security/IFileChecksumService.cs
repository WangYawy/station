
namespace Station.Domain.Security;
/// <summary>
/// 文件校验计算
/// </summary>
public interface IFileChecksumService
{
    /// <summary>
    /// 计算文件
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    string ComputeFile(string path);
    /// <summary>
    /// 计算流
    /// </summary>
    /// <param name="stream"></param>
    /// <returns></returns>
    string Compute(Stream stream);
    /// <summary>
    /// 计算字符串
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    string ComputeString(string text);
    /// <summary>
    /// 计算基于密钥的哈希消息认证码
    /// </summary>
    /// <param name="key"></param>
    /// <param name="text"></param>
    /// <returns></returns>
    string ComputeHmac(string key, string text);
}
