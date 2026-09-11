using Station.Contracts.Commands;

namespace Station.Application.PlatformSync;

/// <summary>指令签名验签：平台 SM2 私钥签名，采集站公钥验签后才执行。</summary>
public interface ICommandSignatureVerifier
{
    bool Verify(RemoteCommand command);
}
