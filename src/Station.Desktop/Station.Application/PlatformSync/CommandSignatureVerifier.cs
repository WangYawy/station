using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Station.Application.Security;
using Station.Contracts.Commands;
using Station.Domain.Security;

namespace Station.Application.PlatformSync;

public sealed class CommandSignatureVerifier : ICommandSignatureVerifier
{
    private readonly CommandVerifierOptions _options;
    private readonly IReportingSigner _reportSinger;

    public CommandSignatureVerifier(IOptions<CommandVerifierOptions> options, IReportingSigner reportSinger)
    {
        _options = options.Value;
        _reportSinger = reportSinger;
    }

    public async Task<bool> VerifyAsync(RemoteCommand command)
    {
        if (!_options.Required || string.IsNullOrWhiteSpace(_options.PublicKeyFile))
        {
            return true; // 未启用/未配置公钥时跳过（兼容），生产配置 Required=true + PublicKeyPem
        }

        var canonical = RemoteCommandSignature.Canonical(command);
        var (signature, _) = await _reportSinger.SignAsync(canonical);

        return command.Signature.Equals(signature);
    }
}
