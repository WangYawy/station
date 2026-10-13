using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Application.Accounts;
using Station.Application.Accounts.Dtos;
using Station.Data.Paging;
using Station.Infrastructure.Persistence.Users;

namespace Station.Infrastructure.Persistence.Accounts;

public sealed class AccountQueryService : IAccountQueryService
{
    private readonly ISqlSugarClient _db;
    private readonly PagingOptions _paging;

    public AccountQueryService(ISqlSugarClient db, IOptions<PagingOptions> paging)
    {
        _db = db;
        _paging = paging.Value;
    }

    public async Task<PageResult<AccountListItemDto>> PageAsync(
        AccountPageQuery query, CancellationToken ct = default)
    {
        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = Math.Clamp(query.PageSize, 1, _paging.MaxPageSize);
        var now = DateTime.Now;

        var q = _db.Queryable<AccountPo>().Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            q = q.Where(x => x.UserName.Contains(kw));
        }

        if (query.IsEnabled is { } enabled) q = q.Where(x => x.IsEnabled == enabled);
        if (query.IsLocked is { } locked)
        {
            q = locked
                ? q.Where(x => x.LockedUntil != null && x.LockedUntil > now)
                : q.Where(x => x.LockedUntil == null || x.LockedUntil <= now);
        }
        if (query.UserId is { } uid) q = q.Where(x => x.UserId == uid);

        q = q.OrderBy(x => x.Id, OrderByType.Desc);

        RefAsync<int> total = 0;
        var items = await q.Select(x => new AccountListItemDto
        {
            Id = x.Id,
            UserName = x.UserName,
            UserId = x.UserId,
            IsEnabled = x.IsEnabled,
            IsLocked = x.LockedUntil != null && x.LockedUntil > now,
            LockedUntil = x.LockedUntil,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        }).ToPageListAsync(pageIndex, pageSize, total);

        await FillUserNamesAsync(items, ct);

        return new PageResult<AccountListItemDto>
        {
            Items = items,
            Total = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    public async Task<AccountDetailDto?> GetByIdAsync(long id, CancellationToken ct = default)
        => await _db.Queryable<AccountPo>()
            .Where(x => x.Id == id && !x.IsDeleted)
            .Select(x => new AccountDetailDto
            {
                Id = x.Id,
                UserName = x.UserName,
                UserId = x.UserId,
                IsEnabled = x.IsEnabled,
                FailedLoginAttempts = x.FailedLoginAttempts,
                LockedUntil = x.LockedUntil,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .FirstAsync(ct);

    public async Task<AccountDetailDto?> GetByUserNameAsync(
        string userName, CancellationToken ct = default)
        => await _db.Queryable<AccountPo>()
            .Where(x => x.UserName == userName && !x.IsDeleted)
            .Select(x => new AccountDetailDto
            {
                Id = x.Id,
                UserName = x.UserName,
                UserId = x.UserId,
                IsEnabled = x.IsEnabled,
                FailedLoginAttempts = x.FailedLoginAttempts,
                LockedUntil = x.LockedUntil,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .FirstAsync(ct);

    public async Task<IReadOnlyList<AccountListItemDto>> GetByUserIdAsync(
        long userId, CancellationToken ct = default)
    {
        var now = DateTime.Now;

        var list = await _db.Queryable<AccountPo>()
            .Where(x => x.UserId == userId && !x.IsDeleted)
            .OrderBy(x => x.Id, OrderByType.Desc)
            .Select(x => new AccountListItemDto
            {
                Id = x.Id,
                UserName = x.UserName,
                UserId = x.UserId,
                IsEnabled = x.IsEnabled,
                IsLocked = x.LockedUntil != null && x.LockedUntil > now,
                LockedUntil = x.LockedUntil,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .ToListAsync(ct);

        await FillUserNamesAsync(list, ct);
        return list;
    }

    public async Task<IReadOnlyList<AccountOptionDto>> GetOptionsAsync(
        bool onlyEnabled = true, CancellationToken ct = default)
    {
        var q = _db.Queryable<AccountPo>().Where(x => !x.IsDeleted);
        if (onlyEnabled) q = q.Where(x => x.IsEnabled);

        return await q
            .OrderBy(x => x.UserName, OrderByType.Asc)
            .Select(x => new AccountOptionDto
            {
                Id = x.Id,
                UserName = x.UserName,
                UserId = x.UserId
            })
            .ToListAsync(ct);
    }
    public Task<bool> ExistsUserNameAsync(string userName, CancellationToken ct = default)
        => _db.Queryable<AccountPo>()
            .Where(x => x.UserName == userName && !x.IsDeleted)
            .AnyAsync(ct);
    public async Task<int> CountActiveAsync(CancellationToken ct = default)
    {
        var now = DateTime.Now;
        return await _db.Queryable<AccountPo>()
            .Where(x => !x.IsDeleted && x.IsEnabled
                        && (x.LockedUntil == null || x.LockedUntil <= now))
            .CountAsync(ct);
    }

    private async Task FillUserNamesAsync(
        IReadOnlyList<AccountListItemDto> items, CancellationToken ct)
    {
        var userIds = items
            .Where(x => x.UserId is not null)
            .Select(x => x.UserId!.Value)
            .Distinct()
            .ToList();

        if (userIds.Count == 0) return;

        var users = await _db.Queryable<UserPo>()
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);

        var map = users.ToDictionary(x => x.Id, x => x.Name);
        foreach (var item in items)
        {
            if (item.UserId is { } uid && map.TryGetValue(uid, out var name))
            {
                item.UserName2 = name;
            }
        }
    }
}
