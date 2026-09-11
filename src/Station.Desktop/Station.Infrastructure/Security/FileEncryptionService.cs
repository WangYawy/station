using Station.Domain.Security;

namespace Station.Infrastructure.Security;

/// <summary>
/// 文件加密
/// </summary>
public sealed class FileEncryptionService : IFileEncryptionService
{
    private readonly ISm4KeyProvider _keyProvider; // 这个接口现在在 Application 层

    public FileEncryptionService(ISm4KeyProvider keyProvider) => _keyProvider = keyProvider;

    public void EncryptInPlace(string path) => Sm4Crypto.EncryptInPlace(path, _keyProvider.GetKey());
    public Stream CreateDecryptStream(string encryptedPath, long plaintextOffset = 0)
        => Sm4Crypto.CreateDecryptReader(encryptedPath, _keyProvider.GetKey(), plaintextOffset);
}
