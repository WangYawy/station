using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Application.Users.Dtos;
using Station.Application.Users;
using Station.Data.Paging;
using Station.Infrastructure.Persistence.Accounts;
using Station.Infrastructure.Persistence.Departments;
using Station.Infrastructure.Persistence.Roles;

namespace Station.Infrastructure.Persistence.Users;

public sealed class UserQueryService : IUserQueryService
{
    private readonly ISqlSugarClient _db;
    private readonly PagingOptions _paging;

    public UserQueryService(ISqlSugarClient db, IOptions<PagingOptions> paging)
    {
        _db = db;
        _paging = paging.Value;
    }

    public async Task<PageResult<UserListItemDto>> PageAsync(
        UserPageQuery query, CancellationToken ct = default)
    {
        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = Math.Clamp(query.PageSize, 1, _paging.MaxPageSize);

        var q = _db.Queryable<UserPo>();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            q = q.Where(x => x.UserNo.Contains(kw) || x.Name.Contains(kw));
        }

        if (query.IsActive is { } active)
        {
            q = q.Where(x => x.IsActive == active);
        }

        if (query.DeptId is { } deptId)
        {
            if (query.IncludeChildDepts)
            {
                var deptIds = await CollectDescendantDeptIdsAsync(deptId, ct);
                q = q.Where(x => deptIds.Contains(x.DeptId));
            }
            else
            {
                q = q.Where(x => x.DeptId == deptId);
            }
        }

        q = q.OrderBy(x => x.Id, OrderByType.Desc);

        RefAsync<int> total = 0;
        var items = await q.Select(x => new UserListItemDto
        {
            Id = x.Id,
            UserNo = x.UserNo,
            Name = x.Name,
            DeptId = x.DeptId,
            IsActive = x.IsActive,
            CreatedAt = x.CreatedAt
        }).ToPageListAsync(pageIndex, pageSize, total);

        await FillListExtraAsync(items, ct);

        return new PageResult<UserListItemDto>
        {
            Items = items,
            Total = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    public async Task<UserDetailDto?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var dto = await _db.Queryable<UserPo>()
            .Where(x => x.Id == id)
            .Select(x => new UserDetailDto
            {
                Id = x.Id,
                UserNo = x.UserNo,
                Name = x.Name,
                DeptId = x.DeptId,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .FirstAsync(ct);

        if (dto is null) return null;

        await FillDetailExtraAsync(dto, ct);
        return dto;
    }

    public async Task<UserDetailDto?> GetByUserNoAsync(string userNo, CancellationToken ct = default)
    {
        var id = await _db.Queryable<UserPo>()
            .Where(x => x.UserNo == userNo)
            .Select(x => x.Id)
            .FirstAsync(ct);

        return id == 0 ? null : await GetByIdAsync(id, ct);
    }

    // =========================================================
    // 选项 / 映射
    // =========================================================

    public async Task<IReadOnlyList<UserOptionDto>> GetOptionsByDeptAsync(
        long deptId, bool includeChildDepts = false, CancellationToken ct = default)
    {
        var q = _db.Queryable<UserPo>().Where(x => x.IsActive);

        if (includeChildDepts)
        {
            var deptIds = await CollectDescendantDeptIdsAsync(deptId, ct);
            q = q.Where(x => deptIds.Contains(x.DeptId));
        }
        else
        {
            q = q.Where(x => x.DeptId == deptId);
        }

        return await q
            .OrderBy(x => x.UserNo, OrderByType.Asc)
            .Select(x => new UserOptionDto
            {
                Id = x.Id,
                UserNo = x.UserNo,
                Name = x.Name,
                DeptId = x.DeptId
            })
            .ToListAsync(ct);
    }

    public async Task<Dictionary<long, string>> GetNameMapAsync(
        IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return new Dictionary<long, string>();

        var list = await _db.Queryable<UserPo>()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);

        return list.ToDictionary(x => x.Id, x => x.Name);
    }

    public async Task<Dictionary<string, long>> GetIdMapByUserNoAsync(
        IReadOnlyCollection<string> userNos, CancellationToken ct = default)
    {
        if (userNos.Count == 0) return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        var list = await _db.Queryable<UserPo>()
            .Where(x => userNos.Contains(x.UserNo))
            .Select(x => new { x.UserNo, x.Id })
            .ToListAsync(ct);

        return list.ToDictionary(x => x.UserNo, x => x.Id, StringComparer.OrdinalIgnoreCase);
    }

    public Task<bool> ExistsAsync(long id, CancellationToken ct = default)
        => _db.Queryable<UserPo>().Where(x => x.Id == id).AnyAsync(ct);

    public Task<bool> ExistsByUserNoAsync(string userNo, CancellationToken ct = default)
    => _db.Queryable<UserPo>().Where(x => x.UserNo == userNo).AnyAsync(ct);



    private async Task FillListExtraAsync(
        IReadOnlyList<UserListItemDto> items, CancellationToken ct)
    {
        if (items.Count == 0) return;

        var deptIds = items.Select(x => x.DeptId).Distinct().ToList();
        var userIds = items.Select(x => x.Id).ToList();

        // 部门名称
        var deptMap = await _db.Queryable<DeptPo>()
            .Where(x => deptIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);
        var deptNameById = deptMap.ToDictionary(x => x.Id, x => x.Name);

        // 账号数
        var accountCounts = await _db.Queryable<AccountPo>()
            .Where(x => x.UserId != null && userIds.Contains(x.UserId.Value) && !x.IsDeleted)
            .GroupBy(x => x.UserId!.Value)
            .Select(x => new { UserId = x.UserId, Count = SqlFunc.AggregateCount(x.Id) })
            .ToListAsync(ct);
        var accountCountByUser = accountCounts.ToDictionary(x => x.UserId!.Value, x => x.Count);

        foreach (var item in items)
        {
            if (deptNameById.TryGetValue(item.DeptId, out var deptName))
                item.DeptName = deptName;

            if (accountCountByUser.TryGetValue(item.Id, out var count))
                item.AccountCount = count;
        }
    }

    private async Task FillDetailExtraAsync(UserDetailDto dto, CancellationToken ct)
    {
        // 部门名称
        dto.DeptName = await _db.Queryable<DeptPo>()
            .Where(x => x.Id == dto.DeptId)
            .Select(x => x.Name)
            .FirstAsync(ct);

        // 账号列表
        dto.Accounts = await _db.Queryable<AccountPo>()
            .Where(x => x.UserId == dto.Id && !x.IsDeleted)
            .Select(x => new UserAccountBriefDto
            {
                Id = x.Id,
                UserName = x.UserName,
                IsEnabled = x.IsEnabled,
                LockedUntil = x.LockedUntil
            })
            .ToListAsync(ct);

        // 角色列表（JOIN user_role + role）
        var roles = await _db.Queryable<UserRolePo>()
            .LeftJoin<RolePo>((ur, r) => ur.RoleId == r.Id)
            .Where((ur, r) => ur.UserId == dto.Id && r.IsActive)
            .Select((ur, r) => new UserRoleBriefDto
            {
                Id = r.Id,
                Code = r.Code,
                Name = r.Name,
                DataScope = r.DataScope
            })
            .ToListAsync(ct);

        dto.Roles = roles;
    }

    private async Task<List<long>> CollectDescendantDeptIdsAsync(
        long rootDeptId, CancellationToken ct)
    {
        var all = await _db.Queryable<DeptPo>()
            .Where(x => x.IsActive)
            .Select(x => new { x.Id, x.ParentId })
            .ToListAsync(ct);

        var childrenByParent = all
            .Where(x => x.ParentId is not null)
            .GroupBy(x => x.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

        var result = new List<long> { rootDeptId };
        var queue = new Queue<long>();
        queue.Enqueue(rootDeptId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!childrenByParent.TryGetValue(current, out var children)) continue;
            foreach (var child in children)
            {
                if (result.Contains(child)) continue;
                result.Add(child);
                queue.Enqueue(child);
            }
        }

        return result;
    }
}
