using Microsoft.Extensions.Options;
using Renci.SshNet;
using Station.Domain.Storage;
using Station.Domain.Security;
using Station.Infrastructure.Security;

namespace Station.Infrastructure.Storage;

/// <summary>
/// SFTP 存储目标：分块写入 + FileMode.Append 实现断点续传，远端校验大小。
/// </summary>
public sealed class SftpStorageTarget : IStorageTarget
{
    private readonly StorageTargetConfig _targetConfig;
    private readonly StorageOptions _globalOptions;
    private readonly ISecretProtector _secretProtector;


    public SftpStorageTarget(StorageTargetConfig targetConfig, IOptions<StorageOptions> options, ISecretProtector secretProtector)
    {
        _targetConfig = targetConfig;
        _globalOptions = options.Value;
        _secretProtector = secretProtector;
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
        var buffer = new byte[_globalOptions.ChunkBytes];
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

    public Task<string> ComputeRemoteSm3Async(string remotePath, CancellationToken cancellationToken)
    {
        using var sftp = CreateClient();
        sftp.Connect();
        using var stream = sftp.OpenRead(AbsolutePath(ResolveRoot(sftp), remotePath));
        var sm3 = Sm3Checksum.Compute(stream);
        sftp.Disconnect();
        return Task.FromResult(sm3);
    }

    private string ResolveRoot(SftpClient sftp) =>
        string.IsNullOrWhiteSpace(_targetConfig.SftpRoot)
            ? sftp.WorkingDirectory.TrimEnd('/')
            : _targetConfig.SftpRoot.TrimEnd('/');

    private static string AbsolutePath(string root, string remotePath) =>
        $"{root}/{remotePath.TrimStart('/')}";

    private static string RemoteDirectory(string remotePath)
    {
        var index = remotePath.LastIndexOf('/');
        return index > 0 ? remotePath[..index] : "/";
    }

    private SftpClient CreateClient()
    {
        var user = _targetConfig.SftpUser ?? string.Empty;
        var password = _secretProtector.TryUnprotect(_targetConfig.SftpPassword) ?? string.Empty;
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
            _targetConfig.SftpHost,
            _targetConfig.SftpPort,
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
