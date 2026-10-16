using System.Text.Json;
using Station.Contracts.Alerts;
using Station.Contracts.Registration;
using Station.Contracts.Reporting;
using Station.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Station.Data.IdGeneration;
using Station.Domain.Outbox;

namespace Station.Application.PlatformSync;

public sealed class SyncOutboxService : ISyncOutboxService
{
    private readonly ISyncOutboxRepository _outbox;
    private readonly ISyncOutboxSyncRepository _outboxSync;
    private readonly IPlatformClient _client;
    private readonly IIdGenerator _idGenerator;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public SyncOutboxService(
        ISyncOutboxRepository outbox,
        ISyncOutboxSyncRepository outboxSync,
        IPlatformClient client,
        IIdGenerator idGenerator,
        IServiceScopeFactory scopeFactory)
    {
        _outbox = outbox;
        _outboxSync = outboxSync;
        _client = client;
        _idGenerator = idGenerator;
        _scopeFactory = scopeFactory;
    }

    public async Task<long> EnqueueAsync(string topic, string payloadJson)
    {
        var entry = SyncOutboxItem.Create(_idGenerator.NewId(), topic, payloadJson, DateTime.Now);

        await _outbox.AddAsync(entry);
        return entry.Id;
    }

    public async Task<int> DrainAsync(int maxItems = 50)
    {
        using var scope = _scopeFactory.CreateScope();
        var outboxRepo = scope.ServiceProvider.GetRequiredService<ISyncOutboxSyncRepository>();
        var now = DateTime.Now;
        var pending = outboxRepo.TakePending(maxItems)
            .ToList();

        var sent = 0;
        foreach (var item in pending)
        {
            try
            {
                await SendAsync(item);
                item.MarkSent(DateTime.Now);
                sent++;
            }
            catch (Exception ex)
            {
                item.MarkFailed(ex.Message, 3, DateTime.Now);
            }
            outboxRepo.Update(item);
        }

        return sent;
    }

    public int CountPendingAsync() => _outboxSync.CountPending();

    private async Task SendAsync(SyncOutboxItem item)
    {
        switch (item.Topic)
        {
            case "register":
                var register = JsonSerializer.Deserialize<StationRegistrationRequest>(item.PayloadJson, _json)
                               ?? throw new InvalidOperationException("注册载荷无效");
                await _client.RegisterAsync(register, CancellationToken.None);
                break;
            case "file-metadata":
                var metadata = JsonSerializer.Deserialize<FileMetadataReport>(item.PayloadJson, _json)
                               ?? throw new InvalidOperationException("元数据载荷无效");
                await _client.ReportMetadataAsync(metadata, CancellationToken.None);
                break;
            case "alert":
                var alert = JsonSerializer.Deserialize<AlertReport>(item.PayloadJson, _json)
                            ?? throw new InvalidOperationException("报警载荷无效");
                await _client.ReportAlertAsync(alert, CancellationToken.None);
                break;
            case "license-status":
                var license = JsonSerializer.Deserialize<Station.Contracts.Reporting.LicenseStatusReport>(item.PayloadJson, _json)
                              ?? throw new InvalidOperationException("授权状态载荷无效");
                await _client.ReportLicenseStatusAsync(license, CancellationToken.None);
                break;
            case "command-result":
                var envelope = JsonSerializer.Deserialize<CommandResultEnvelope>(item.PayloadJson, _json)
                               ?? throw new InvalidOperationException("回执载荷无效");
                await _client.ReportCommandResultAsync(envelope.StationId, envelope.Result, CancellationToken.None);
                break;
            default:
                throw new InvalidOperationException($"未知主题 {item.Topic}");
        }
    }
}
