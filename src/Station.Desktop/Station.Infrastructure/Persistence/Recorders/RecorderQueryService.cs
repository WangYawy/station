using SqlSugar;
using Station.Application.Recorders;
using Station.Application.Recorders.Dtos;
using Station.Infrastructure.Persistence.Departments;
using Station.Infrastructure.Persistence.Users;

namespace Station.Infrastructure.Persistence.Recorders;

public sealed class RecorderQueryService : IRecorderQueryService
{
    private readonly ISqlSugarClient _db;

    public RecorderQueryService(ISqlSugarClient db) => _db = db;

    public async Task<RecorderListItemDto?> GetByIdAsync(long id, CancellationToken ct = default)
        => await _db.Queryable<RecorderPo>()
            .Where(x => x.Id == id)
            .Select(x => new RecorderListItemDto
            {
                Id = x.Id,
                SerialNumber = x.SerialNumber,
                Model = x.Model,
                Protocol = x.Protocol,
                BoundUserId = x.BoundUserId,
                DeptId = x.DeptId,
                IsAuthorized = x.IsAuthorized,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt
            })
            .FirstAsync(ct);

    public async Task<RecorderListItemDto?> GetBySerialAsync(string serialNumber, CancellationToken ct = default)
        => await _db.Queryable<RecorderPo>()
            .Where(x => x.SerialNumber == serialNumber)
            .Select(x => new RecorderListItemDto
            {
                Id = x.Id,
                SerialNumber = x.SerialNumber,
                Model = x.Model,
                Protocol = x.Protocol,
                BoundUserId = x.BoundUserId,
                DeptId = x.DeptId,
                IsAuthorized = x.IsAuthorized,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt
            })
            .FirstAsync(ct);

    public async Task<RecorderListItemDto?> GetByCodeAsync(string code, CancellationToken ct = default)
        => await _db.Queryable<RecorderPo>()
            .Where(x => x.Code == code)
            .Select(x => new RecorderListItemDto
            {
                Id = x.Id,
                SerialNumber = x.SerialNumber,
                Model = x.Model,
                Protocol = x.Protocol,
                BoundUserId = x.BoundUserId,
                DeptId = x.DeptId,
                IsAuthorized = x.IsAuthorized,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt
            })
            .FirstAsync(ct);

    public async Task<RecorderListItemDto?> GetByNameAsync(string name, CancellationToken ct = default)
        => await _db.Queryable<RecorderPo>()
            .Where(x => x.Name == name)
            .Select(x => new RecorderListItemDto
            {
                Id = x.Id,
                SerialNumber = x.SerialNumber,
                Model = x.Model,
                Protocol = x.Protocol,
                BoundUserId = x.BoundUserId,
                DeptId = x.DeptId,
                IsAuthorized = x.IsAuthorized,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt
            })
            .FirstAsync(ct);

    public async Task<IReadOnlyList<RecorderListItemDto>> GetListAsync(
        bool onlyActive = true, CancellationToken ct = default)
    {
        var q = _db.Queryable<RecorderPo>();
        if (onlyActive) q = q.Where(x => x.IsActive);

        var list = await q
            .OrderBy(x => x.SerialNumber, OrderByType.Asc)
            .Select(x => new RecorderListItemDto
            {
                Id = x.Id,
                SerialNumber = x.SerialNumber,
                Model = x.Model,
                Protocol = x.Protocol,
                BoundUserId = x.BoundUserId,
                DeptId = x.DeptId,
                IsAuthorized = x.IsAuthorized,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(ct);

        await FillNamesAsync(list, ct);
        return list;
    }

    public Task<bool> ExistsSerialAsync(string serialNumber, CancellationToken ct = default)
        => _db.Queryable<RecorderPo>().Where(x => x.SerialNumber == serialNumber).AnyAsync(ct);
    public Task<bool> ExistsCodeAsync(string code, CancellationToken ct = default)
       => _db.Queryable<RecorderPo>().Where(x => x.Code == code).AnyAsync(ct);

    private async Task FillNamesAsync(IReadOnlyList<RecorderListItemDto> list, CancellationToken ct)
    {
        if (list.Count == 0) return;

        var userIds = list.Where(x => x.BoundUserId is not null)
            .Select(x => x.BoundUserId!.Value).Distinct().ToList();
        var deptIds = list.Where(x => x.DeptId is not null)
            .Select(x => x.DeptId!.Value).Distinct().ToList();

        if (userIds.Count > 0)
        {
            var users = await _db.Queryable<UserPo>()
                .Where(x => userIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name })
                .ToListAsync(ct);
            var userMap = users.ToDictionary(x => x.Id, x => x.Name);
            foreach (var item in list)
                if (item.BoundUserId is { } uid && userMap.TryGetValue(uid, out var n))
                    item.BoundUserName = n;
        }

        if (deptIds.Count > 0)
        {
            var depts = await _db.Queryable<DeptPo>()
                .Where(x => deptIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name })
                .ToListAsync(ct);
            var deptMap = depts.ToDictionary(x => x.Id, x => x.Name);
            foreach (var item in list)
                if (item.DeptId is { } did && deptMap.TryGetValue(did, out var n))
                    item.DeptName = n;
        }
    }
}
