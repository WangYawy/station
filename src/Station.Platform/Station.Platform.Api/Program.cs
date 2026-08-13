using Station.Platform.Api;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Station.Application;
using Station.Infrastructure;
using Station.Infrastructure.Backup;
using Station.Infrastructure.Db;
using Station.Platform.Domain.Entities;
using Station.Domain.Entities;
using Station.Infrastructure.Persistence;
using SqlSugar;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
{
    options.InputFormatters.Insert(0, new TextPlainInputFormatter());
});
builder.Services.AddHttpClient();
builder.Services.AddStationDatabase(builder.Configuration);
builder.Services.AddStationApplication(builder.Configuration);
// 平台端连接管理：共享 SqlSugarScope 单例在 async 线程切换下会泄漏 MySQL/PostgreSQL/Kingbase 连接，
// 改为按请求创建独立客户端（auto-close），随请求作用域释放；备份服务同步改为 scoped，避免单例捕获请求级客户端。
builder.Services.AddScoped<ISqlSugarClient>(sp =>
    sp.GetRequiredService<ISqlSugarFactory>().CreateClient(sp.GetRequiredService<DbOptions>()));
builder.Services.AddScoped<IDatabaseBackupService, DatabaseBackupService>();
builder.Services.AddHostedService<PlatformBackupWorker>();
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
        typeof(PlatformRecorder),
        typeof(PlatformFileCorrection),
        typeof(PlatformCommand),
        typeof(PlatformConfigChange),
        typeof(Account), typeof(User), typeof(Dept), typeof(Role),
        typeof(Permission), typeof(RolePermission), typeof(UserRole), typeof(AuditLog));
    initializer.EnsureColumn("platform_station", "LastHeartbeatAt");
    initializer.EnsureColumn("platform_station", "OperationalStatus", "int");
    await scope.ServiceProvider.GetRequiredService<IAuthSeeder>().EnsureAsync();
}

app.Run();
