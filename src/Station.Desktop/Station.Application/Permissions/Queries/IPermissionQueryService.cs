using Station.Application.Permissions.Dtos;
using Station.Data.Paging;

namespace Station.Application.Permissions;

public interface IPermissionQueryService
{
    Task<PageResult<PermissionListItemDto>> PageAsync(
        PermissionPageQuery query, CancellationToken ct = default);

    Task<PermissionListItemDto?> GetByIdAsync(long id, CancellationToken ct = default);

    Task<PermissionListItemDto?> GetByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>全部启用权限，按模块分组（权限目录树用）。</summary>
    Task<IReadOnlyList<PermissionGroupedDto>> GetGroupedCatalogAsync(
        bool onlyActive = true, CancellationToken ct = default);

    /// <summary>按模块过滤的下拉选项。</summary>
    Task<IReadOnlyList<PermissionOptionDto>> GetOptionsAsync(
        string? module = null, CancellationToken ct = default);

    /// <summary>查询指定角色已授予的权限 Id（供角色编辑页勾选）。</summary>
    Task<IReadOnlyList<long>> GetIdsByRoleIdAsync(long roleId, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetCodesByIdsAsync(
        IReadOnlyCollection<long> ids, CancellationToken ct = default);

    Task<bool> ExistsCodeAsync(string code, CancellationToken ct = default);
}
