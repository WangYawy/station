using System.Security.Claims;
using Station.Application.Authorization;
using Station.Application.Authentication;
using Station.Domain.Enums;
using AuthService = Station.Application.Authorization.IAuthorizationService;

namespace Station.Desktop.WebHost.Controllers;

/// <summary>单机 Web 数据范围解析（与平台一致：部门树继承 / 本人）。</summary>
public static class WebScopeHelper
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
