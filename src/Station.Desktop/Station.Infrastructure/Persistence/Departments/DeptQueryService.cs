using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Application.Departments.Dtos;
using Station.Application.Departments;
using Station.Data.Paging;
using Station.Infrastructure.Persistence.Users;

namespace Station.Infrastructure.Persistence.Departments;

public sealed class DeptQueryService : IDeptQueryService
{
    private readonly ISqlSugarClient _db;
    private readonly PagingOptions _paging;

    public DeptQueryService(ISqlSugarClient db, IOptions<PagingOptions> paging)
    {
        _db = db;
        _paging = paging.Value;
    }

    public async Task<PageResult<DeptListItemDto>> PageAsync(
        DeptPageQuery query, CancellationToken ct = default)
    {
        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = Math.Clamp(query.PageSize, 1, _paging.MaxPageSize);

        var q = _db.Queryable<DeptPo>();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            q = q.Where(x => x.Code.Contains(kw) || x.Name.Contains(kw));
        }

        if (query.IsActive is { } active)
        {
            q = q.Where(x => x.IsActive == active);
        }

        if (query.ParentId is { } pid)
        {
            q = q.Where(x => x.ParentId == pid);
        }

        q = q.OrderBy(x => x.SortOrder, OrderByType.Asc)
             .OrderBy(x => x.Id, OrderByType.Asc);

        RefAsync<int> total = 0;
        var items = await q.Select(x => new DeptListItemDto
        {
            Id = x.Id,
            Code = x.Code,
            Name = x.Name,
            ParentId = x.ParentId,
            SortOrder = x.SortOrder,
            IsActive = x.IsActive,
            CreatedAt = x.CreatedAt
        }).ToPageListAsync(pageIndex, pageSize, total);

        await FillParentNameAsync(items, ct);

        return new PageResult<DeptListItemDto>
        {
            Items = items,
            Total = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    public async Task<DeptDetailDto?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var dto = await _db.Queryable<DeptPo>()
            .Where(x => x.Id == id)
            .Select(x => new DeptDetailDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                ParentId = x.ParentId,
                SortOrder = x.SortOrder,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .FirstAsync(ct);

        if (dto is null) return null;

        if (dto.ParentId is { } pid)
        {
            dto.ParentName = await _db.Queryable<DeptPo>()
                .Where(x => x.Id == pid)
                .Select(x => x.Name)
                .FirstAsync(ct);
        }

        dto.ChildCount = await _db.Queryable<DeptPo>()
            .Where(x => x.ParentId == id)
            .CountAsync(ct);

        dto.UserCount = await _db.Queryable<UserPo>()
            .Where(x => x.DeptId == id && x.IsActive)
            .CountAsync(ct);

        return dto;
    }

    public async Task<DeptDetailDto?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        // 复用：先取 Id，再走 GetById
        var id = await _db.Queryable<DeptPo>()
            .Where(x => x.Code == code)
            .Select(x => x.Id)
            .FirstAsync(ct);

        return id == 0 ? null : await GetByIdAsync(id, ct);
    }
    public async Task<IReadOnlyList<DeptOptionDto>> GetOptionsAsync(CancellationToken ct = default)
        => await _db.Queryable<DeptPo>()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder, OrderByType.Asc)
            .OrderBy(x => x.Id, OrderByType.Asc)
            .Select(x => new DeptOptionDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                ParentId = x.ParentId
            })
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DeptTreeNodeDto>> GetTreeAsync(
        bool onlyActive = true, CancellationToken ct = default)
    {
        var q = _db.Queryable<DeptPo>();
        if (onlyActive) q = q.Where(x => x.IsActive);

        var flat = await q
            .OrderBy(x => x.SortOrder, OrderByType.Asc)
            .OrderBy(x => x.Id, OrderByType.Asc)
            .Select(x => new DeptTreeNodeDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                ParentId = x.ParentId,
                SortOrder = x.SortOrder,
                IsActive = x.IsActive
            })
            .ToListAsync(ct);

        var byId = flat.ToDictionary(x => x.Id);
        var roots = new List<DeptTreeNodeDto>();

        foreach (var node in flat)
        {
            if (node.ParentId is { } pid && byId.TryGetValue(pid, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        return roots;
    }

    public async Task<Dictionary<long, string>> GetNameMapAsync(
        IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return new Dictionary<long, string>();

        var list = await _db.Queryable<DeptPo>()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);

        return list.ToDictionary(x => x.Id, x => x.Name);
    }

    public Task<bool> ExistsAsync(long id, CancellationToken ct = default)
        => _db.Queryable<DeptPo>().Where(x => x.Id == id).AnyAsync(ct);

    public Task<bool> ExistsCodeAsync(string code, CancellationToken ct = default)
        => _db.Queryable<DeptPo>().Where(x => x.Code == code).AnyAsync(ct);

    private async Task FillParentNameAsync(
        IReadOnlyList<DeptListItemDto> items, CancellationToken ct)
    {
        var parentIds = items
            .Where(x => x.ParentId is not null)
            .Select(x => x.ParentId!.Value)
            .Distinct()
            .ToList();

        if (parentIds.Count == 0) return;

        var nameMap = await GetNameMapAsync(parentIds, ct);
        foreach (var item in items)
        {
            if (item.ParentId is { } pid && nameMap.TryGetValue(pid, out var name))
            {
                item.ParentName = name;
            }
        }
    }
}
