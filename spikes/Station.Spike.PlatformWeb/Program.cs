using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.RegularExpressions;

// ---------------------------------------------------------------------------
// M25 Spike: 平台前端 Vue 工程化（station-platform-web）
//  - 平台后端静态托管 Vue 构建产物（/ 返回 SPA，资源可访问）
//  - 登录/统计/文件等 API 不受影响
// ---------------------------------------------------------------------------

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

const int PlatformPort = 5113;
var projectDir = @"E:\Reny\station\src\Station.Platform\Station.Platform.Api";
var platformExe = Path.Combine(projectDir, @"bin\Release\net8.0\Station.Platform.Api.exe");
var platform = Process.Start(new ProcessStartInfo
{
    FileName = platformExe,
    Arguments = $"--urls http://127.0.0.1:{PlatformPort}",
    WorkingDirectory = projectDir,
    UseShellExecute = false,
    CreateNoWindow = true,
    Environment =
    {
        ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{PlatformPort}",
        ["STATION__DB__PROVIDER"] = "MySql",
        ["STATION__DB__CONNECTIONSTRING"] = "Server=localhost;Port=3306;Database=station_platform;Uid=station;Pwd=Station@123;Charset=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=None"
    }
});

var deadline = DateTime.Now.AddSeconds(30);
while (DateTime.Now < deadline)
{
    try
    {
        using var probe = new TcpClient();
        probe.Connect("127.0.0.1", PlatformPort);
        break;
    }
    catch
    {
        await Task.Delay(500);
    }
}

try
{
    using var http = new HttpClient
    {
        BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}"),
        Timeout = TimeSpan.FromSeconds(10)
    };

    // ---------- 1. / 返回 Vue 构建入口 ----------
    try
    {
        var home = await http.GetAsync("/");
        var html = await home.Content.ReadAsStringAsync();
        var isVueSpa = html.Contains("<div id=\"app\">", StringComparison.OrdinalIgnoreCase) &&
                       Regex.IsMatch(html, @"src=""\.?/assets/index-[^""]+\.js""");
        Pass("根路径返回Vue入口", home.StatusCode == HttpStatusCode.OK && isVueSpa,
            $"HTTP {(int)home.StatusCode}, 包含 #app={html.Contains("<div id=\"app\">")}, 含构建资源={Regex.IsMatch(html, @"src=""\.?/assets/index-[^""]+\.js""")}");
    }
    catch (Exception ex)
    {
        Pass("根路径返回Vue入口", false, ex.Message);
    }

    // ---------- 2. 构建 JS 资源可访问 ----------
    try
    {
        var html = await (await http.GetAsync("/")).Content.ReadAsStringAsync();
        var match = Regex.Match(html, @"src=""([^""]*assets/index-[^""]+\.js)""");
        var asset = await http.GetAsync(match.Groups[1].Value);
        Pass("构建JS资源可访问", asset.StatusCode == HttpStatusCode.OK,
            $"{match.Groups[1].Value} -> HTTP {(int)asset.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("构建JS资源可访问", false, ex.Message);
    }

    // ---------- 3. 登录与受保护 API 正常 ----------
    try
    {
        using var admin = new HttpClient(new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer()
        })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{PlatformPort}")
        };
        var login = await admin.PostAsJsonAsync("/api/v1/auth/login", new { userName = "admin", password = "Admin@123" });
        var stats = login.StatusCode == HttpStatusCode.OK
            ? await admin.GetAsync("/api/v1/stats/overview")
            : new HttpResponseMessage(HttpStatusCode.BadRequest);
        Pass("登录+统计API正常", login.StatusCode == HttpStatusCode.OK && stats.StatusCode == HttpStatusCode.OK,
            $"login={(int)login.StatusCode}, stats/overview={(int)stats.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("登录+统计API正常", false, ex.Message);
    }

    // ---------- 4. 未登录访问受保护 API -> 401 ----------
    try
    {
        var resp = await http.GetAsync("/api/v1/files?page=1&size=10");
        Pass("未登录API401", resp.StatusCode == HttpStatusCode.Unauthorized, $"HTTP {(int)resp.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("未登录API401", false, ex.Message);
    }
}
finally
{
    if (platform is not null && !platform.HasExited)
    {
        platform.Kill();
    }
}

Console.WriteLine();
Console.WriteLine("================ M25 平台前端 Vue 工程化 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;
