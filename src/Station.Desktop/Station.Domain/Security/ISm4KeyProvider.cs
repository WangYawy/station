
namespace Station.Domain.Security;

/// <summary>SM4 密钥来源：本机密钥文件（Windows 用 DPAPI 保护，Linux 0600）。</summary>
public interface ISm4KeyProvider
{
    byte[] GetKey();
}
