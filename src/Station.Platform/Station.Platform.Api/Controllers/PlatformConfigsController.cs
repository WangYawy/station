using Microsoft.AspNetCore.Mvc;
using Station.Contracts;
using Station.Contracts.Api;
using Station.Contracts.Sync;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Repositories;
using Station.Platform.Domain.Entities;

namespace Station.Platform.Api.Controllers;

/// <summary>平台配置发布与查询。</summary>
[ApiController]
[Route("api/v1/stations/{stationId:long}/configs")]
public class PlatformConfigsController : ControllerBase
{
    private readonly IRepository<PlatformConfigChange> _changes;
    private readonly IIdGenerator _idGenerator;

    public PlatformConfigsController(
        IRepository<PlatformConfigChange> changes,
        IIdGenerator idGenerator)
    {
        _changes = changes;
        _idGenerator = idGenerator;
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<long>>> Publish(
        long stationId,
        PublishConfigRequest request)
    {
        var existing = await _changes.GetListAsync(c => c.StationId == stationId);
        var nextVersion = existing.Count == 0 ? 1 : existing.Max(c => c.Version) + 1;
        await _changes.InsertAsync(new PlatformConfigChange
        {
            Id = _idGenerator.NextId(),
            StationId = stationId,
            EntityType = request.EntityType,
            Operation = request.Operation,
            PayloadJson = request.PayloadJson,
            Version = nextVersion,
            PublishedAt = DateTime.Now
        });
        return Ok(ApiResponse<long>.Ok(nextVersion));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ConfigChangeItem>>>> List(long stationId)
    {
        var list = (await _changes.GetListAsync(c => c.StationId == stationId))
            .OrderBy(c => c.Version)
            .Select(ToItem)
            .ToList();
        return Ok(ApiResponse<List<ConfigChangeItem>>.Ok(list));
    }

    internal static ConfigChangeItem ToItem(PlatformConfigChange c) => new()
    {
        EntityType = c.EntityType,
        Operation = c.Operation,
        PayloadJson = c.PayloadJson,
        Version = c.Version
    };
}

public sealed record PublishConfigRequest(
    string EntityType,
    SyncOperation Operation = SyncOperation.Upsert,
    string PayloadJson = "{}");
