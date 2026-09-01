using Station.Domain.Security;

namespace Station.Infrastructure.Security;
public sealed class LicenseSignatureService : ILicenseSignatureService
{
    public string Sign(string privateKeyPem, string text) => Sm2LicenseSigner.Sign(privateKeyPem, text);
    public bool Verify(string publicKeyPem, string text, string signature) => Sm2LicenseSigner.Verify(publicKeyPem, text, signature);
    public string DerivePublicKey(string privateKeyPem) => Sm2LicenseSigner.DerivePublicKey(privateKeyPem);
}
