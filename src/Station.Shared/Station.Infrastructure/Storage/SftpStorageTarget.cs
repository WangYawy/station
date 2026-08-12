using Microsoft.Extensions.Options;
using Renci.SshNet;

namespace Station.Infrastructure.Storage;

/// <summary>
/// SFTP 存储目标：分块写入 + FileMode.Append 实现断点续传，远端校验大小。
/// </summary>
public sealed class SftpStorageTarget : IStorageTarget
{
    private readonly StorageOptions _options;

    public SftpStorageTarget(IOptions<StorageOptions> options)
    {
        _options = options.Value;
    }

    public string Name => "sftp";

    public async Task UploadAsync(
        UploadTargetFile file,
        Func<double, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        using var sftp = CreateClient();
        sftp.Connect();
        EnsureRemoteDirectories(sftp, RemoteDirectory(file.RemotePath));

        using var remoteStream = sftp.Open(file.RemotePath, FileMode.Append, FileAccess.Write);
        await using var localStream = File.OpenRead(file.LocalPath);
        var buffer = new byte[_options.ChunkBytes];
        long total = 0;
        int read;
        while ((read = await localStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            remoteStream.Write(buffer, 0, read);
            total += read;
            if (file.Size > 0)
            {
                onProgress?.Invoke((double)total / file.Size);
            }
        }

        if (file.Size > 0)
        {
            onProgress?.Invoke(1);
        }

        sftp.Disconnect();
    }

    public Task<long> GetRemoteSizeAsync(string remotePath, CancellationToken cancellationToken)
    {
        using var sftp = CreateClient();
        sftp.Connect();
        var length = sftp.GetAttributes(remotePath).Size;
        sftp.Disconnect();
        return Task.FromResult(length);
    }

    private static string RemoteDirectory(string remotePath)
    {
        var index = remotePath.LastIndexOf('/');
        return index > 0 ? remotePath[..index] : "/";
    }

    private SftpClient CreateClient()
    {
        var user = _options.SftpUser ?? string.Empty;
        var password = _options.SftpPassword ?? string.Empty;
        var passwordAuth = new Renci.SshNet.PasswordAuthenticationMethod(user, password);
        var keyboardAuth = new Renci.SshNet.KeyboardInteractiveAuthenticationMethod(user);
        keyboardAuth.AuthenticationPrompt += (_, e) =>
        {
            foreach (var prompt in e.Prompts)
            {
                prompt.Response = password;
            }
        };

        var connectionInfo = new Renci.SshNet.ConnectionInfo(
            _options.SftpHost,
            _options.SftpPort,
            user,
            passwordAuth,
            keyboardAuth);
        return new SftpClient(connectionInfo);
    }

    private static void EnsureRemoteDirectories(SftpClient sftp, string remoteDirectory)
    {
        var current = string.Empty;
        foreach (var segment in remoteDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + segment;
            if (!sftp.Exists(current))
            {
                sftp.CreateDirectory(current);
            }
        }
    }
}
