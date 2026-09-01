
namespace Station.Domain.Security;

public interface IFileEncryptionService
{
    /// <summary>加密缓存文件（原地加密或指定目标）</summary>
    void EncryptInPlace(string path);
    /// <summary>创建解密读取流</summary>
    Stream CreateDecryptStream(string encryptedPath, long plaintextOffset = 0);
}
