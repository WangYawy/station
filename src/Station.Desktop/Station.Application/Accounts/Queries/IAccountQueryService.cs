using Station.Application.Accounts.Dtos;
using Station.Data.Paging;

namespace Station.Application.Accounts;

public interface IAccountQueryService
{
    Task<PageResult<AccountListItemDto>> PageAsync(
        AccountPageQuery query, CancellationToken ct = default);

    Task<AccountDetailDto?> GetByIdAsync(long id, CancellationToken ct = default);

    Task<AccountDetailDto?> GetByUserNameAsync(string userName, CancellationToken ct = default);

    /// <summary>列出指定用户下的所有账号（一个人员可挂多账号）。</summary>
    Task<IReadOnlyList<AccountListItemDto>> GetByUserIdAsync(
        long userId, CancellationToken ct = default);

    /// <summary>下拉选项（一般用于平台端、管理端的选择器）。</summary>
    Task<IReadOnlyList<AccountOptionDto>> GetOptionsAsync(
        bool onlyEnabled = true, CancellationToken ct = default);

    /// <summary>存在性校验。</summary>
    Task<bool> ExistsUserNameAsync(string userName, CancellationToken ct = default);

    /// <summary>统计当前启用、未锁定账号数（自检、运营看板）。</summary>
    Task<int> CountActiveAsync(CancellationToken ct = default);
}
