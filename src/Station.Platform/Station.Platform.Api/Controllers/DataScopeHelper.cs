using System.Security.Claims;
using Station.Application.Authorization;
using Station.Domain.Enums;
using AuthService = Station.Application.Authorization.IAuthorizationService;

namespace Station.Platform.Api.Controllers;

/// <summary>从当前登录用户解析数据权限范围（部门树：本部门及下级 / 仅本部门 / 仅本人 / 全量）。</summary>
public static class DataScopeHelper
{
    public static async Task<DataScopeResult> GetScopeAsync(
        ClaimsPrincipal user,
        AuthService authorization,
        IDataScopeProvider dataScope)
    {
        if (user.FindFirst("accountId") is not { } accountClaim ||
            !long.TryParse(accountClaim.Value, out var accountId))
        {
            return new DataScopeResult(DataScope.Self, false, [], 0);
        }

        var session = await authorization.GetSessionAsync(accountId);
        if (session.UserId is not { } userId)
        {
            return new DataScopeResult(DataScope.Self, false, [], 0);
        }

        return await dataScope.GetDataScopeAsync(userId);
    }
}
