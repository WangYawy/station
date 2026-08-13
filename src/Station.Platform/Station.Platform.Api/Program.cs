using Station.Platform.Api;
using Station.Infrastructure;
using Station.Platform.Domain.Entities;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddStationDatabase(builder.Configuration);

var app = builder.Build();

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
        typeof(PlatformCommand));
}

app.Run();
