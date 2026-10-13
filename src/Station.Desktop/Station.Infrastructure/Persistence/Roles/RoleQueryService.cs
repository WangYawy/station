using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Application.Roles.Dtos;
using Station.Application.Roles;
using Station.Data.Paging;
using Station.Infrastructure.Persistence.Permissions;
using Station.Infrastructure.Persistence.Users;

namespace Station.Infrastructure.Persistence.Roles;

public sealed class RoleQueryService : IRoleQueryService
{
    private readonly ISqlSugarClient _db;
    private readonly PagingOptions _paging;

    public RoleQueryService(ISqlSugarClient db, IOptions<PagingOptions> paging)
    {
        _db = db;
        _paging = paging.Value;
    }

    public async Task<PageResult<RoleListItemDto>> PageAsync(
        RolePageQuery query, CancellationToken ct = default)
    {
        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = Math.Clamp(query.PageSize, 1, _paging.MaxPageSize);

        var q = _db.Queryable<RolePo>();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            q = q.Where(x => x.Code.Contains(kw) || x.Name.Contains(kw));
        }

        if (query.IsActive is { } active) q = q.Where(x => x.IsActive == active);
        if (query.IsSystem is { } sys) q = q.Where(x => x.IsSystem == sys);

        q = q.OrderBy(x => x.Id, OrderByType.Desc);

        RefAsync<int> total = 0;
        var items = await q.Select(x => new RoleListItemDto
        {
            Id = x.Id,
            Code = x.Code,
            Name = x.Name,
            DataScope = x.DataScope,
            IsSystem = x.IsSystem,
            IsActive = x.IsActive,
            CreatedAt = x.CreatedAt
        }).ToPageListAsync(pageIndex, pageSize, total);

        await FillCountsAsync(items, ct);

        return new PageResult<RoleListItemDto>
        {
            Items = items,
            Total = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    public async Task<RoleDetailDto?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var dto = await _db.Queryable<RolePo>()
            .Where(x => x.Id == id)
            .Select(x => new RoleDetailDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                DataScope = x.DataScope,
                IsSystem = x.IsSystem,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .FirstAsync(ct);

        if (dto is null) return null;

        await FillPermissionsAsync(dto, ct);
        return dto;
    }

    public async Task<RoleDetailDto?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var id = await _db.Queryable<RolePo>()
            .Where(x => x.Code == code)
            .Select(x => x.Id)
            .FirstAsync(ct);

        return id == 0 ? null : await GetByIdAsync(id, ct);
    }

    public async Task<IReadOnlyList<RoleOptionDto>> GetOptionsAsync(CancellationToken ct = default)
        => await _db.Queryable<RolePo>()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Id, OrderByType.Asc)
            .Select(x => new RoleOptionDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                DataScope = x.DataScope
            })
            .ToListAsync(ct);

    public async Task<Dictionary<long, string>> GetNameMapAsync(
        IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return new Dictionary<long, string>();

        var list = await _db.Queryable<RolePo>()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);

        return list.ToDictionary(x => x.Id, x => x.Name);
    }

    public async Task<IReadOnlyList<RoleOptionDto>> GetByUserIdAsync(
        long userId, CancellationToken ct = default)
        => await _db.Queryable<UserRolePo>()
            .LeftJoin<RolePo>((ur, r) => ur.RoleId == r.Id)
            .Where((ur, r) => ur.UserId == userId && r.IsActive)
            .OrderBy((ur, r) => r.Id, OrderByType.Asc)
            .Select((ur, r) => new RoleOptionDto
            {
                Id = r.Id,
                Code = r.Code,
                Name = r.Name,
                DataScope = r.DataScope
            })
            .ToListAsync(ct);


    public Task<bool> ExistsAsync(long id, CancellationToken ct = default)
        => _db.Queryable<RolePo>().Where(x => x.Id == id).AnyAsync(ct);

    public Task<bool> ExistsCodeAsync(string code, CancellationToken ct = default)
        => _db.Queryable<RolePo>().Where(x => x.Code == code).AnyAsync(ct);

    private async Task FillCountsAsync(
        IReadOnlyList<RoleListItemDto> items, CancellationToken ct)
    {
        if (items.Count == 0) return;

        var roleIds = items.Select(x => x.Id).ToList();

        var permCounts = await _db.Queryable<RolePermissionPo>()
            .Where(x => roleIds.Contains(x.RoleId))
            .GroupBy(x => x.RoleId)
            .Select(x => new { RoleId = x.RoleId, Count = SqlFunc.AggregateCount(x.Id) })
            .ToListAsync(ct);
        var permMap = permCounts.ToDictionary(x => x.RoleId, x => x.Count);

        var userCounts = await _db.Queryable<UserRolePo>()
            .Where(x => roleIds.Contains(x.RoleId))
            .GroupBy(x => x.RoleId)
            .Select(x => new { RoleId = x.RoleId, Count = SqlFunc.AggregateCount(x.Id) })
            .ToListAsync(ct);
        var userMap = userCounts.ToDictionary(x => x.RoleId, x => x.Count);

        foreach (var item in items)
        {
            if (permMap.TryGetValue(item.Id, out var pc)) item.PermissionCount = pc;
            if (userMap.TryGetValue(item.Id, out var uc)) item.UserCount = uc;
        }
    }

    private async Task FillPermissionsAsync(RoleDetailDto dto, CancellationToken ct)
    {
        var permissions = await _db.Queryable<RolePermissionPo>()
            .LeftJoin<PermissionPo>((rp, p) => rp.PermissionId == p.Id)
            .Where((rp, p) => rp.RoleId == dto.Id)
            .OrderBy((rp, p) => p.Code, OrderByType.Asc)
            .Select((rp, p) => new RolePermissionBriefDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                Module = p.Module
            })
            .ToListAsync(ct);

        dto.Permissions = permissions;
        dto.PermissionIds = permissions.Select(x => x.Id).ToList();
    }
}
