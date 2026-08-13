using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Station.Application.Audit;
using Station.Domain.Entities;

namespace Station.Desktop.WebHost;

/// <summary>单机版内置 Web 宿主配置：Controller 分层，业务逻辑下沉 Application。</summary>
public class Startup
{
    private readonly IConfiguration _configuration;

    public Startup(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.Configure<WebOptions>(_configuration.GetSection(WebOptions.SectionName));
        services.AddControllers(options =>
        {
            options.InputFormatters.Insert(0, new TextPlainInputFormatter());
        });
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "station_web_auth";
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });
        services.AddAuthorization();
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        var webOptions = _configuration.GetSection(WebOptions.SectionName).Get<WebOptions>() ?? new WebOptions();
        if (webOptions.EnableLan)
        {
            app.Use(async (context, next) =>
            {
                await next();
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    var audit = context.RequestServices.GetService<IAuditLogService>();
                    if (audit is not null)
                    {
                        await audit.WriteAsync(new AuditLog
                        {
                            OperatorAccount = context.User.Identity?.IsAuthenticated == true
                                ? context.User.Identity.Name
                                : null,
                            OperationType = "web.access",
                            Target = context.Request.Path,
                            SourceIp = context.Connection.RemoteIpAddress?.ToString(),
                            Detail = $"{context.Request.Method} {context.Response.StatusCode}",
                            Result = context.Response.StatusCode < 400 ? 1 : 0
                        });
                    }
                }
            });
        }

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }
}
