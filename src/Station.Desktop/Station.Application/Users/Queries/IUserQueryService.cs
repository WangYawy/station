using Station.Application.Users.Dtos;
using Station.Data.Paging;

namespace Station.Application.Users;

public interface IUserQueryService
{
    Task<PageResult<UserListItemDto>> PageAsync(
        UserPageQuery query, CancellationToken ct = default);

    Task<UserDetailDto?> GetByIdAsync(long id, CancellationToken ct = default);

    Task<UserDetailDto?> GetByUserNoAsync(string userNo, CancellationToken ct = default);

    

    /// <summary>按部门列用户（用于填充"部门下人员"下拉/列表）。</summary>
    Task<IReadOnlyList<UserOptionDto>> GetOptionsByDeptAsync(
        long deptId, bool includeChildDepts = false, CancellationToken ct = default);

    /// <summary>批量获取 Id → 姓名映射（报表填充用）。</summary>
    Task<Dictionary<long, string>> GetNameMapAsync(
        IReadOnlyCollection<long> ids, CancellationToken ct = default);

    /// <summary>批量获取工号 → Id 映射（平台同步场景常用）。</summary>
    Task<Dictionary<string, long>> GetIdMapByUserNoAsync(
        IReadOnlyCollection<string> userNos, CancellationToken ct = default);

    /// <summary>存在性校验（写流程用）。</summary>
    Task<bool> ExistsAsync(long id, CancellationToken ct = default);

    Task<bool> ExistsByUserNoAsync(string userNo, CancellationToken ct = default);

}
