using Station.Application.Roles.Dtos;
using Station.Data.Paging;

namespace Station.Application.Roles;

public interface IRoleQueryService
{
    Task<PageResult<RoleListItemDto>> PageAsync(
        RolePageQuery query, CancellationToken ct = default);

    Task<RoleDetailDto?> GetByIdAsync(long id, CancellationToken ct = default);

    Task<RoleDetailDto?> GetByCodeAsync(string code, CancellationToken ct = default);

    Task<IReadOnlyList<RoleOptionDto>> GetOptionsAsync(CancellationToken ct = default);

    /// <summary>批量获取角色 Id → 名称映射。</summary>
    Task<Dictionary<long, string>> GetNameMapAsync(
        IReadOnlyCollection<long> ids, CancellationToken ct = default);

    /// <summary>查询指定用户挂载的角色（用于用户详情页）。</summary>
    Task<IReadOnlyList<RoleOptionDto>> GetByUserIdAsync(
        long userId, CancellationToken ct = default);

    /// <summary>存在性校验（写流程用）。</summary>
    Task<bool> ExistsAsync(long id, CancellationToken ct = default);

    Task<bool> ExistsCodeAsync(string code, CancellationToken ct = default);
}
