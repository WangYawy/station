using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Station.Application.Authentication;
using AuthService = Station.Application.Authorization.IAuthorizationService;
using AppAuthService = Station.Application.Authentication.IAuthenticationService;

namespace Station.Desktop.WebHost.Controllers;

/// <summary>单机 Web 登录认证（同一套账号体系，本地验证，失败锁定复用服务层）。</summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly AppAuthService _authentication;
    private readonly AuthService _authorization;

    public AuthController(AppAuthService authentication, AuthService authorization)
    {
        _authentication = authentication;
        _authorization = authorization;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var result = await _authentication.LoginAsync(request with
        {
            SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString()
        });
        if (!result.Success || result.Session is null)
        {
            var message = result.FailureReason switch
            {
                LoginFailureReason.Disabled => "账号已停用",
                LoginFailureReason.LockedOut => "账号已锁定，请稍后再试",
                _ => "用户名或密码错误"
            };
            return Unauthorized(new { message });
        }

        var session = result.Session;
        var claims = new List<Claim>
        {
            new("accountId", session.AccountId.ToString()),
            new(ClaimTypes.Name, session.UserName)
        };
        claims.AddRange(session.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        return Ok(new { session });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { success = true });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var accountId = long.Parse(User.FindFirst("accountId")!.Value);
        return Ok(new { session = await _authorization.GetSessionAsync(accountId) });
    }
}
