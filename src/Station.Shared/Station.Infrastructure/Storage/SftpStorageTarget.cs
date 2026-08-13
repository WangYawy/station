using Microsoft.Extensions.Options;
using Renci.SshNet;
using Station.Infrastructure.Security;

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
        var root = ResolveRoot(sftp);
        var remotePath = AbsolutePath(root, file.RemotePath);
        EnsureRemoteDirectories(sftp, RemoteDirectory(remotePath));

        using var remoteStream = sftp.Open(remotePath, FileMode.Append, FileAccess.Write);
        await using var localStream = file.LocalStreamFactory?.Invoke() ?? File.OpenRead(file.LocalPath);
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

        remoteStream.Flush();
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
        var length = sftp.GetAttributes(AbsolutePath(ResolveRoot(sftp), remotePath)).Size;
        sftp.Disconnect();
        return Task.FromResult(length);
    }

    private string ResolveRoot(SftpClient sftp) =>
        string.IsNullOrWhiteSpace(_options.SftpRoot)
            ? sftp.WorkingDirectory.TrimEnd('/')
            : _options.SftpRoot.TrimEnd('/');

    private static string AbsolutePath(string root, string remotePath) =>
        $"{root}/{remotePath.TrimStart('/')}";

    private static string RemoteDirectory(string remotePath)
    {
        var index = remotePath.LastIndexOf('/');
        return index > 0 ? remotePath[..index] : "/";
    }

    private SftpClient CreateClient()
    {
        var user = _options.SftpUser ?? string.Empty;
        var password = Sm4SecretProtector.TryUnprotect(_options.SftpPassword) ?? string.Empty;
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
