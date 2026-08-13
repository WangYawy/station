using Station.Platform.Api;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Station.Application;
using Station.Infrastructure;
using Station.Platform.Domain.Entities;
using Station.Domain.Entities;
using Station.Infrastructure.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddStationDatabase(builder.Configuration);
builder.Services.AddStationApplication(builder.Configuration);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "station_platform_auth";
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
    initializer.EnsureCreated(
        typeof(PlatformStation),
        typeof(PlatformFileMetadata),
        typeof(PlatformAlertReport),
        typeof(PlatformCommand),
        typeof(PlatformConfigChange),
        typeof(Account), typeof(User), typeof(Dept), typeof(Role),
        typeof(Permission), typeof(RolePermission), typeof(UserRole), typeof(AuditLog));
    initializer.EnsureColumn("platform_station", "LastHeartbeatAt");
    await scope.ServiceProvider.GetRequiredService<IAuthSeeder>().EnsureAsync();
}

app.Run();
