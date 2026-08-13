using Microsoft.Extensions.Options;
using Station.Contracts.Commands;
using Station.Infrastructure.Security;

namespace Station.Application.PlatformSync;

public sealed class CommandSignatureVerifier : ICommandSignatureVerifier
{
    private readonly CommandVerifierOptions _options;

    public CommandSignatureVerifier(IOptions<CommandVerifierOptions> options)
    {
        _options = options.Value;
    }

    public bool Verify(RemoteCommand command)
    {
        if (!_options.Required || string.IsNullOrWhiteSpace(_options.PublicKeyPem))
        {
            return true; // 未启用/未配置公钥时跳过（兼容），生产配置 Required=true + PublicKeyPem
        }

        return Sm2LicenseSigner.Verify(
            _options.PublicKeyPem,
            RemoteCommandSignature.Canonical(command),
            command.Signature);
    }
}
