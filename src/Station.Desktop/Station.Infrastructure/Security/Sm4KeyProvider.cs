using System.Security.Cryptography;
using Station.Domain.Security;

namespace Station.Infrastructure.Security;

public sealed class Sm4KeyProvider : ISm4KeyProvider
{
    /// <summary>进程内默认实例（凭据/授权信息等静态解密使用）。</summary>
    public static Sm4KeyProvider Default { get; } = new();

    private readonly Lazy<byte[]> _key;

    public Sm4KeyProvider(string? keyFile = null)
    {
        _key = new Lazy<byte[]>(() => LoadOrCreate(keyFile));
    }

    public byte[] GetKey() => _key.Value;

    private static byte[] LoadOrCreate(string? keyFile)
    {
        var path = keyFile ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Station", "keys", "sm4.key");
        if (File.Exists(path))
        {
            return ReadKey(path);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var key = RandomNumberGenerator.GetBytes(16);
        var stored = OperatingSystem.IsWindows()
            ? ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser)
            : key;
        File.WriteAllBytes(path, stored);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return key;
    }

    private static byte[] ReadKey(string path)
    {
        var stored = File.ReadAllBytes(path);
        return OperatingSystem.IsWindows()
            ? ProtectedData.Unprotect(stored, null, DataProtectionScope.CurrentUser)
            : stored;
    }
}
