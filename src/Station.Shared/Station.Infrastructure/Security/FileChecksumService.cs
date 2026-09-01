
using Station.Domain.Security;

namespace Station.Infrastructure.Security;

public sealed class FileChecksumService : IFileChecksumService
{
    public string ComputeFile(string path) => Sm3Checksum.ComputeFile(path);
    public string Compute(Stream stream) => Sm3Checksum.Compute(stream);
    public string ComputeString(string text) => Sm3Checksum.ComputeString(text);
    public string ComputeHmac(string key, string text) => Sm3Checksum.ComputeHmac(key, text);
}
