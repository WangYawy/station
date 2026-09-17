using System.Net.NetworkInformation;
using System.Net.Sockets;
using Station.Application.Collecting;
using Station.Application.Settings;
using Station.Application.Storage;
using Microsoft.Extensions.Logging;
using Station.Domain.Storage;
using Station.Domain.Collecting;

namespace Station.Application.Settings;

/// <summary>设备自检实现（对应原型"设备自检"分组）。</summary>
public sealed class SystemSelfCheckService : ISystemSelfCheckService
{
    private readonly CollectOptions _collect;
    private readonly IStorageConfiguration _storage;
    private readonly IReadOnlyList<IStorageTarget> _targets;
    private readonly ICollectSource _source;
    private readonly ILogger<SystemSelfCheckService> _logger;

    public SystemSelfCheckService(
        CollectOptions collect,
        IStorageConfiguration storage,
        IReadOnlyList<IStorageTarget> targets,
        ICollectSource source,
        ILogger<SystemSelfCheckService> logger)
    {
        _collect = collect;
        _storage = storage;
        _source = source;
        _targets = targets;
        _logger = logger;
    }

    public Task<IReadOnlyList<SelfCheckItemDto>> RunAsync()
    {
        var items = new List<SelfCheckItemDto>
        {
            CheckDevice(),
            CheckDisk(),
            CheckNetwork(),
            CheckStorageTarget()
        };
        _logger.LogInformation("设备自检完成：{Ok}/{Total} 项正常", items.Count(i => i.Ok), items.Count);
        return Task.FromResult<IReadOnlyList<SelfCheckItemDto>>(items);
    }

    private SelfCheckItemDto CheckDevice()
    {
        try
        {
            var root = _source.GetRecorderRoot(
                new CollectDeviceInfo("自检", "SELF-CHECK", Station.Contracts.ProtocolType.Ums));
            return new SelfCheckItemDto("采集设备", true, $"设备在线（{root}）");
        }
        catch (Exception ex)
        {
            return new SelfCheckItemDto("采集设备", false, ex.Message);
        }
    }

    private SelfCheckItemDto CheckDisk()
    {
        var probePath = Path.Combine(_collect.CacheDirectory, ".selfcheck-probe");
        try
        {
            Directory.CreateDirectory(_collect.CacheDirectory);
            File.WriteAllText(probePath, DateTime.Now.ToString("O"));
            var content = File.ReadAllText(probePath);
            File.Delete(probePath);
            return string.IsNullOrEmpty(content)
                ? new SelfCheckItemDto("磁盘读写", false, "读写校验内容为空")
                : new SelfCheckItemDto("磁盘读写", true, $"缓存目录可读写（{_collect.CacheDirectory}）");
        }
        catch (Exception ex)
        {
            return new SelfCheckItemDto("磁盘读写", false, $"缓存目录不可写：{ex.Message}");
        }
    }

    private static SelfCheckItemDto CheckNetwork()
    {
        try
        {
            var gateway = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(ni => ni.GetIPProperties().GatewayAddresses)
                .Select(g => g.Address)
                .FirstOrDefault();
            if (gateway is null)
            {
                return new SelfCheckItemDto("网络连接", false, "未发现可用网关（网线/无线未连接）");
            }

            using var ping = new Ping();
            var reply = ping.Send(gateway, 2000);
            return reply is { Status: IPStatus.Success }
                ? new SelfCheckItemDto("网络连接", true, $"网关可达（{gateway}，{reply.RoundtripTime}ms）")
                : new SelfCheckItemDto("网络连接", false, $"网关不可达（{gateway}，{reply?.Status}）");
        }
        catch (Exception ex)
        {
            return new SelfCheckItemDto("网络连接", false, ex.Message);
        }
    }

    private SelfCheckItemDto CheckStorageTarget()
    {
        try
        {
            foreach (var target in _targets)
            {
                return target.Name switch
                {
                    //StorageTargetKind.Local => CheckLocalTarget(),
                    //StorageTargetKind.Ftp => CheckRemoteTarget(_storage.FtpHost, _storage.FtpPort, "FTP"),
                    //StorageTargetKind.Sftp => CheckRemoteTarget(_storage.SftpHost, _storage.SftpPort, "SFTP"),
                    _ => new SelfCheckItemDto("存储目标", false, $"未知存储类型 {target.Name}")
                };
            }

            return new SelfCheckItemDto("存储目标", false, "未知");
        }
        catch (Exception ex)
        {
            return new SelfCheckItemDto("存储目标", false, ex.Message);
        }
    }

    //private SelfCheckItemDto CheckLocalTarget()
    //{
    //    var probe = Path.Combine(_storage.LocalRoot, ".selfcheck-probe");
    //    try
    //    {
    //        Directory.CreateDirectory(_storage.LocalRoot);
    //        File.WriteAllText(probe, "ok");
    //        File.Delete(probe);
    //        return new SelfCheckItemDto("存储目标", true, $"本地磁盘可写（{_storage.LocalRoot}）");
    //    }
    //    catch (Exception ex)
    //    {
    //        return new SelfCheckItemDto("存储目标", false, $"本地磁盘不可写：{ex.Message}");
    //    }
    //}

    private static SelfCheckItemDto CheckRemoteTarget(string host, int port, string protocol)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return new SelfCheckItemDto("存储目标", false, $"{protocol} 主机未配置");
        }

        using var client = new TcpClient();
        var task = client.ConnectAsync(host, port);
        if (!task.Wait(3000))
        {
            return new SelfCheckItemDto("存储目标", false, $"{protocol} {host}:{port} 连接超时");
        }

        return client.Connected
            ? new SelfCheckItemDto("存储目标", true, $"{protocol} 端口可达（{host}:{port}）")
            : new SelfCheckItemDto("存储目标", false, $"{protocol} {host}:{port} 连接失败");
    }
}
