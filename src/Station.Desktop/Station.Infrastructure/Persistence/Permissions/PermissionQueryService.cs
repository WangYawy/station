using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Application.Permissions.Dtos;
using Station.Application.Permissions;
using Station.Data.Paging;
using Station.Infrastructure.Persistence.Roles;

namespace Station.Infrastructure.Persistence.Permissions;

public sealed class PermissionQueryService : IPermissionQueryService
{
    private readonly ISqlSugarClient _db;
    private readonly PagingOptions _paging;

    public PermissionQueryService(ISqlSugarClient db, IOptions<PagingOptions> paging)
    {
        _db = db;
        _paging = paging.Value;
    }

    public async Task<PageResult<PermissionListItemDto>> PageAsync(
        PermissionPageQuery query, CancellationToken ct = default)
    {
        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = Math.Clamp(query.PageSize, 1, _paging.MaxPageSize);

        var q = _db.Queryable<PermissionPo>();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            q = q.Where(x => x.Code.Contains(kw) || x.Name.Contains(kw));
        }

        if (!string.IsNullOrWhiteSpace(query.Module))
        {
            q = q.Where(x => x.Module == query.Module);
        }

        if (query.IsActive is { } active)
        {
            q = q.Where(x => x.IsActive == active);
        }

        q = q.OrderBy(x => x.Module, OrderByType.Asc)
             .OrderBy(x => x.Code, OrderByType.Asc);

        RefAsync<int> total = 0;
        var items = await q.Select(x => new PermissionListItemDto
        {
            Id = x.Id,
            Code = x.Code,
            Name = x.Name,
            Module = x.Module,
            IsActive = x.IsActive,
            CreatedAt = x.CreatedAt
        }).ToPageListAsync(pageIndex, pageSize, total);

        return new PageResult<PermissionListItemDto>
        {
            Items = items,
            Total = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    public async Task<PermissionListItemDto?> GetByIdAsync(long id, CancellationToken ct = default)
        => await _db.Queryable<PermissionPo>()
            .Where(x => x.Id == id)
            .Select(x => new PermissionListItemDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Module = x.Module,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt
            })
            .FirstAsync(ct);

    public async Task<PermissionListItemDto?> GetByCodeAsync(
        string code, CancellationToken ct = default)
        => await _db.Queryable<PermissionPo>()
            .Where(x => x.Code == code)
            .Select(x => new PermissionListItemDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Module = x.Module,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt
            })
            .FirstAsync(ct);

    public async Task<IReadOnlyList<PermissionGroupedDto>> GetGroupedCatalogAsync(
        bool onlyActive = true, CancellationToken ct = default)
    {
        var q = _db.Queryable<PermissionPo>();
        if (onlyActive) q = q.Where(x => x.IsActive);

        var flat = await q
            .OrderBy(x => x.Module, OrderByType.Asc)
            .OrderBy(x => x.Code, OrderByType.Asc)
            .Select(x => new PermissionListItemDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Module = x.Module,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(ct);

        return flat
            .GroupBy(x => x.Module)
            .Select(g => new PermissionGroupedDto
            {
                Module = g.Key,
                Items = g.ToList()
            })
            .ToList();
    }

    public async Task<IReadOnlyList<PermissionOptionDto>> GetOptionsAsync(
        string? module = null, CancellationToken ct = default)
    {
        var q = _db.Queryable<PermissionPo>().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(module))
        {
            q = q.Where(x => x.Module == module);
        }

        return await q
            .OrderBy(x => x.Module, OrderByType.Asc)
            .OrderBy(x => x.Code, OrderByType.Asc)
            .Select(x => new PermissionOptionDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Module = x.Module
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<long>> GetIdsByRoleIdAsync(
        long roleId, CancellationToken ct = default)
    {
        var list = await _db.Queryable<RolePermissionPo>()
            .Where(x => x.RoleId == roleId)
            .Select(x => x.PermissionId)
            .ToListAsync(ct);
        return list;
    }

    public async Task<IReadOnlyList<string>> GetCodesByIdsAsync(
        IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return Array.Empty<string>();

        return await _db.Queryable<PermissionPo>()
            .Where(x => ids.Contains(x.Id))
            .Select(x => x.Code)
            .ToListAsync(ct);
    }

    public Task<bool> ExistsCodeAsync(string code, CancellationToken ct = default)
        => _db.Queryable<PermissionPo>().Where(x => x.Code == code).AnyAsync(ct);
}
