using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Station.Application.Security;
using Station.Contracts.Commands;
using Station.Domain.Security;

namespace Station.Application.PlatformSync;

public sealed class CommandSignatureVerifier : ICommandSignatureVerifier
{
    private readonly CommandVerifierOptions _options;
    private readonly ILicenseCryptoService _licenseCrypto;

    public CommandSignatureVerifier(IOptions<CommandVerifierOptions> options, ILicenseCryptoService licenseCrypto)
    {
        _options = options.Value;
        _licenseCrypto = licenseCrypto;
    }

    public async Task<bool> VerifyAsync(RemoteCommand command)
    {
        if (!_options.Required || string.IsNullOrWhiteSpace(_options.PublicKeyFile))
        {
            return true; // 未启用/未配置公钥时跳过（兼容），生产配置 Required=true + PublicKeyPem
        }

        var canonical = RemoteCommandSignature.Canonical(command);
        var (signature, _) = await _licenseCrypto.SignReportingAsync(canonical);

        return command.Signature.Equals(signature);
    }
}
