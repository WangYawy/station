using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Station.Application.Licensing;
using Station.Application.PlatformSync;
using Station.Contracts.Registration;
using Station.Contracts.Reporting;
using Station.Infrastructure.Licensing;
using Station.Infrastructure.Security;

namespace Station.Desktop.Infrastructure;

/// <summary>
/// 平台同步后台任务：注册上报（一次）→ 补报 Outbox → 指令轮询执行回执；
/// 上报失败留在 Outbox 由下轮补报（断网恢复自动续传）。
/// </summary>
public sealed class PlatformSyncWorkerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlatformOptions _options;
    private readonly IStationContext _stationContext;
    private readonly IMachineFingerprintProvider _fingerprint;
    private DateTime _lastCommandPoll = DateTime.MinValue;

    public PlatformSyncWorkerHostedService(
        IServiceScopeFactory scopeFactory,
        IStationContext stationContext,
        IMachineFingerprintProvider fingerprint,
        IOptions<PlatformOptions> options)
    {
        _scopeFactory = scopeFactory;
        _stationContext = stationContext;
        _fingerprint = fingerprint;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.Enabled && !string.IsNullOrWhiteSpace(_options.BaseUrl))
                {
                    await RunSyncAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // 平台不可达等异常不中断后台服务
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(3, _options.SyncIntervalSeconds)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task RunSyncAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IPlatformClient>();
        var outbox = scope.ServiceProvider.GetRequiredService<ISyncOutboxService>();
        var commands = scope.ServiceProvider.GetRequiredService<ICommandService>();

        if (_stationContext.StationId is null)
        {
            var registration = new StationRegistrationRequest
            {
                StationCode = _options.StationCode,
                MachineFingerprint = _fingerprint.CollectParts(),
                OsVersion = Environment.OSVersion.VersionString,
                CpuArch = RuntimeInformation.ProcessArchitecture.ToString(),
                SoftwareVersion = "0.1.0",
                UsbPortCount = 0,
                StationBaseUrl = _options.StationBaseUrl
            };
            var response = await client.RegisterAsync(registration, ct);
            if (response is not null)
            {
                _stationContext.Set(response.StationId);
            }
        }

        if (_stationContext.StationId is { } stationId)
        {
            // 授权状态心跳上报（SM2 签名）
            var license = scope.ServiceProvider.GetRequiredService<ILicenseService>();
            var reporting = scope.ServiceProvider.GetRequiredService<ReportingOptions>();
            var check = await license.CheckAsync();
            var statusReport = new LicenseStatusReport
            {
                StationId = stationId,
                LicenseStatus = check.Status,
                ExpiresAt = check.ExpiresAt,
                DaysLeft = check.DaysLeft
            };
            if (!string.IsNullOrWhiteSpace(reporting.PrivateKeyPem))
            {
                statusReport = statusReport with
                {
                    Signature = Sm2LicenseSigner.Sign(
                        reporting.PrivateKeyPem,
                        LicenseStatusReportSignature.Canonical(statusReport))
                };
            }

            await scope.ServiceProvider.GetRequiredService<ISyncOutboxService>()
                .EnqueueAsync("license-status", JsonSerializer.Serialize(statusReport));

            await outbox.DrainAsync();

            var configSync = await client.SyncConfigAsync(
                new Station.Contracts.Sync.ConfigSyncRequest
                {
                    StationId = stationId,
                    AppliedVersions = new Dictionary<string, long>(
                        scope.ServiceProvider.GetRequiredService<IConfigSyncState>().AppliedVersions)
                },
                ct);
            if (configSync is not null)
            {
                await scope.ServiceProvider.GetRequiredService<IConfigApplyService>()
                    .ApplyAsync(configSync);
                scope.ServiceProvider.GetRequiredService<IConfigSyncState>().MarkSynced();
            }

            if (DateTime.Now - _lastCommandPoll >= TimeSpan.FromSeconds(Math.Max(3, _options.CommandPollIntervalSeconds)))
            {
                await commands.PollAndExecuteAsync(stationId);
                _lastCommandPoll = DateTime.Now;
            }
        }
    }
}
