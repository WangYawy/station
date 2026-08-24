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
using Station.Platform.Api.Realtime;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, _, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            Path.Combine(AppContext.BaseDirectory, "logs", "platform-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}"));

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
builder.Services.AddSignalR();
builder.Services.AddSingleton<IRealtimeEventBus, RealtimeEventBus>();
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
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<StationHub>("/hubs/stations");

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
        typeof(PlatformEmergencyTask),
        typeof(Account), typeof(User), typeof(Dept), typeof(Role),
        typeof(Permission), typeof(RolePermission), typeof(UserRole), typeof(AuditLog));
    initializer.EnsureColumn("platform_station", "LastHeartbeatAt");
    initializer.EnsureColumn("platform_station", "OperationalStatus", "int");
    await scope.ServiceProvider.GetRequiredService<IAuthSeeder>().EnsureAsync();
}

app.Run();
