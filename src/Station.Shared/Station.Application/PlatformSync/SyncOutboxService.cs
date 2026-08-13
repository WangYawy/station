using System.Text.Json;
using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Contracts.Alerts;
using Station.Contracts.Commands;
using Station.Contracts.Registration;
using Station.Contracts.Reporting;
using Station.Domain.Entities;
using Station.Infrastructure;
using Station.Infrastructure.Db;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Repositories;

namespace Station.Application.PlatformSync;

public sealed class SyncOutboxService : ISyncOutboxService
{
    private readonly IRepository<SyncOutbox> _outbox;
    private readonly IPlatformClient _client;
    private readonly IIdGenerator _idGenerator;
    private readonly ISqlSugarFactory _sqlSugarFactory;
    private readonly DbOptions _dbOptions;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public SyncOutboxService(
        IRepository<SyncOutbox> outbox,
        IPlatformClient client,
        IIdGenerator idGenerator,
        ISqlSugarFactory sqlSugarFactory,
        DbOptions dbOptions)
    {
        _outbox = outbox;
        _client = client;
        _idGenerator = idGenerator;
        _sqlSugarFactory = sqlSugarFactory;
        _dbOptions = dbOptions;
    }

    public async Task<long> EnqueueAsync(string topic, string payloadJson)
    {
        var entry = new SyncOutbox
        {
            Id = _idGenerator.NextId(),
            Topic = topic,
            PayloadJson = payloadJson,
            Status = 0,
            CreatedAt = DateTime.Now
        };
        await _outbox.InsertAsync(entry);
        return entry.Id;
    }

    public async Task<int> DrainAsync(int maxItems = 50)
    {
        using var client = _sqlSugarFactory.CreateClient(_dbOptions, autoCloseConnection: false);
        var now = DateTime.Now;
        var pending = client.Queryable<SyncOutbox>()
            .Where(o => o.Status == 0 && (o.NextRetryAt == null || o.NextRetryAt <= now))
            .OrderBy(o => o.Id)
            .Take(maxItems)
            .ToList();

        var sent = 0;
        foreach (var item in pending)
        {
            try
            {
                await SendAsync(item);
                item.Status = 1;
                item.SentAt = DateTime.Now;
                item.Error = null;
                sent++;
            }
            catch (Exception ex)
            {
                item.RetryCount++;
                item.Error = ex.Message;
                item.NextRetryAt = DateTime.Now.AddSeconds(Math.Min(300, 15 * Math.Pow(2, Math.Min(item.RetryCount, 5))));
            }

            client.Updateable(item).ExecuteCommand();
        }

        return sent;
    }

    public async Task<int> CountPendingAsync() =>
        await _outbox.CountAsync(o => o.Status == 0);

    private async Task SendAsync(SyncOutbox item)
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
