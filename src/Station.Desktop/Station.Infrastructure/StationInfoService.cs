
using Microsoft.Extensions.Logging;
using Station.Application;
using Station.Data.Repositories;
using Station.Data.SqlSugar.Extensions;
using Station.Domain.Entities;

namespace Station.Infrastructure;

public class StationInfoService : IStationInfoService
{
    private readonly IRepository<StationInfo> _repo;
    private readonly ILogger<StationInfoService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private volatile StationInfoDto? _current;

    public StationInfoService(IRepository<StationInfo> repo, ILogger<StationInfoService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public StationInfoDto Current => _current ?? throw new InvalidOperationException(
            "采集站信息尚未初始化。");

    public bool IsPlatformMode => Current.IsPlatformMode;

    public event EventHandler<StationInfoDto>? InfoChanged;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        StationInfoDto snapshot;

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entity = await _repo.AsQueryable()
                .OrderByDescending(x => x.UpdatedAt)
                .FirstAsync()
                .ConfigureAwait(false);
            if (entity is null)
            {
                throw new InvalidOperationException("采集站信息尚未初始化");
            }
            snapshot = MapToSnapshot(entity);
            _current = snapshot;
        }
        finally
        {
            _writeLock.Release();
        }

        RaiseInfoChanged(snapshot);
    }

    public async Task UpdateAsync(StationInfoDto info, CancellationToken ct = default)
    {
        StationInfoDto snapshot;

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existing = await _repo.AsQueryable()
                .Where(x => x.StationUid == info.StationUid)
                .FirstAsync()
                .ConfigureAwait(false);

            if (existing is null)
            {
                var newEntity = MapToEntity(info);
                newEntity.UpdatedAt = DateTime.UtcNow;
                await _repo.InsertAsync(newEntity).ConfigureAwait(false);
                snapshot = MapToSnapshot(newEntity);
            }
            else
            {
                var merged = MapToEntity(info);
                merged.Id = existing.Id;
                merged.OperatingSystem = existing.OperatingSystem; // OS 本地采集，界面不可改
                merged.PlatformVersion = existing.PlatformVersion;  // 本地保存不递增平台版本
                merged.UpdatedAt = DateTime.UtcNow;

                await _repo.UpdateAsync(merged)
                    .ConfigureAwait(false);

                snapshot = MapToSnapshot(merged);
            }

            _current = snapshot;
        }
        finally
        {
            _writeLock.Release();
        }

        _logger.LogInformation("采集站信息已更新 StationUid={Uid}", snapshot.StationUid);
        RaiseInfoChanged(snapshot);
    }

    public async Task ApplyPlatformPushAsync(StationInfoDto info, long platformVersion,
        CancellationToken ct = default)
    {
        StationInfoDto snapshot;
        bool changed = false;

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existing = await _repo.FirstAsync(null!, ct)
                .ConfigureAwait(false);

            if (existing is not null && platformVersion <= existing.PlatformVersion)
            {
                _logger.LogInformation(
                    "平台下发版本 {New} 未超过本地 {Local}，忽略本次更新。",
                    platformVersion, existing.PlatformVersion);
                snapshot = MapToSnapshot(existing);
            }
            else if (existing is null)
            {
                var newEntity = MapToEntity(info);
                newEntity.OperatingSystem = "";
                newEntity.PlatformVersion = platformVersion;
                newEntity.UpdatedAt = DateTime.UtcNow;

                await _repo.InsertAsync(newEntity).ConfigureAwait(false);

                snapshot = MapToSnapshot(newEntity);
                changed = true;
            }
            else
            {
                var merged = MapToEntity(info);
                merged.Id = existing.Id;
                merged.StationUid = info.StationUid; // 平台可分配 StationUid
                merged.OperatingSystem = existing.OperatingSystem; // 保留本地 OS
                merged.PlatformVersion = platformVersion;
                merged.UpdatedAt = DateTime.UtcNow;

                await _repo.UpdateAsync(merged)
                    .ConfigureAwait(false);

                snapshot = MapToSnapshot(merged);
                changed = true;
            }

            _current = snapshot;
        }
        finally
        {
            _writeLock.Release();
        }

        if (changed)
        {
            _logger.LogInformation(
                "平台下发已应用 StationUid={Uid}, PlatformVersion={Version}",
                snapshot.StationUid, platformVersion);
            RaiseInfoChanged(snapshot);
        }
    }

    #region // 内部工具
    private void RaiseInfoChanged(StationInfoDto snapshot)
       => InfoChanged?.Invoke(this, snapshot);

    private static StationInfoDto MapToSnapshot(StationInfo e) =>
        new(e.StationUid, e.StationCode, e.StationName,
            e.ProductSerialNumber, e.Model, e.OperatingSystem,
            e.UsbPortCount, e.Location, e.Department, e.ManagementMode);

    private static StationInfo MapToEntity(StationInfoDto i) => new()
    {
        StationUid = i.StationUid,
        StationCode = i.StationCode,
        StationName = i.StationName,
        ProductSerialNumber = i.ProductSerialNumber,
        Model = i.Model,
        OperatingSystem = i.OperatingSystem,
        UsbPortCount = i.UsbPortCount,
        Location = i.Location,
        Department = i.Department,
        ManagementMode = i.ManagementMode
    };

    #endregion
}
