using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Station.Application.Authentication;
using AuthService = Station.Application.Authentication.IAuthenticationService;
using AppAuthorization = Station.Application.Authorization.IAuthorizationService;

namespace Station.Platform.Api.Controllers;

/// <summary>平台登录认证（共享账号体系 + Cookie）。</summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authentication;
    private readonly AppAuthorization _authorization;

    public AuthController(
        AuthService authentication,
        AppAuthorization authorization)
    {
        _authentication = authentication;
        _authorization = authorization;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var result = await _authentication.LoginAsync(request);
        if (!result.Success || result.Session is null)
        {
            return Unauthorized(new { message = "用户名或密码错误" });
        }

        var session = result.Session;
        var claims = new List<Claim>
        {
            new("accountId", session.AccountId.ToString()),
            new(ClaimTypes.Name, session.UserName)
        };
        claims.AddRange(session.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
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
        var session = await _authorization.GetSessionAsync(accountId);
        return Ok(new { session });
    }
}
