namespace Station.Domain.Security;
/// <summary>哈希计算服务（用于签名/校验）</summary>
public interface IHashService
{
    /// <summary>计算字符串的哈希值（十六进制小写）</summary>
    string ComputeHash(string text);
    string ComputeFileHash(string filePath);
}
