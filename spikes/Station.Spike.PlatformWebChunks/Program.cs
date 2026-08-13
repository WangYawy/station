using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.RegularExpressions;

// ---------------------------------------------------------------------------
// M27 Spike: 前端代码分割 + ECharts 按需引入
//  - 入口/供应商/Element Plus/ECharts 分块，视图按路由懒加载
//  - 平台托管所有构建资源可访问，API 回归正常
// ---------------------------------------------------------------------------

var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

const int PlatformPort = 5116;
var projectDir = @"E:\Reny\station\src\Station.Platform\Station.Platform.Api";
var assetsDir = Path.Combine(projectDir, "wwwroot", "assets");
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

    var html = await (await http.GetAsync("/")).Content.ReadAsStringAsync();
    var entryMatch = Regex.Match(html, @"src=""([^""]*assets/index-[^""]+\.js)""");

    // ---------- 1. 入口与分块结构（磁盘静态检查） ----------
    try
    {
        var jsFiles = Directory.GetFiles(assetsDir, "*.js").Select(f => Path.GetFileName(f)!).ToList();
        var entrySize = new FileInfo(Path.Combine(assetsDir, Path.GetFileName(entryMatch.Groups[1].Value))).Length;
        var hasEcharts = jsFiles.Any(f => f.StartsWith("echarts-"));
        var hasVueVendor = jsFiles.Any(f => f.StartsWith("vue-vendor-"));
        var hasElementPlus = jsFiles.Any(f => f.StartsWith("element-plus-"));
        var hasLazyViews = jsFiles.Any(f => f.StartsWith("StatsView-")) &&
                           jsFiles.Any(f => f.StartsWith("FilesView-")) &&
                           jsFiles.Any(f => f.StartsWith("LoginView-"));
        var echartsSize = hasEcharts ? new FileInfo(Path.Combine(assetsDir, jsFiles.First(f => f.StartsWith("echarts-")))).Length : 0;
        Pass("分块结构", html.Contains("vue-vendor", StringComparison.OrdinalIgnoreCase) &&
                         entrySize < 100 * 1024 &&
                         hasEcharts && hasVueVendor && hasElementPlus && hasLazyViews,
            $"入口 {entrySize / 1024.0:F1}KB, echarts {echartsSize / 1024.0:F1}KB, 视图懒加载={hasLazyViews}");
    }
    catch (Exception ex)
    {
        Pass("分块结构", false, ex.Message);
    }

    // ---------- 2. 全部构建资源可访问 ----------
    try
    {
        var assets = Directory.GetFiles(assetsDir)
            .Select(Path.GetFileName)
            .Where(f => f!.EndsWith(".js") || f.EndsWith(".css"))
            .Select(f => f!)
            .ToList();
        var failedAssets = new List<string>();
        foreach (var asset in assets)
        {
            var resp = await http.GetAsync($"/assets/{asset}");
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                failedAssets.Add(asset);
            }
        }

        Pass("构建资源全部可访问", failedAssets.Count == 0,
            $"共 {assets.Count} 个资源，失败 {failedAssets.Count} 个");
    }
    catch (Exception ex)
    {
        Pass("构建资源全部可访问", false, ex.Message);
    }

    // ---------- 3. 登录 + 统计 API 回归 ----------
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
        Pass("登录+统计API回归", login.StatusCode == HttpStatusCode.OK && stats.StatusCode == HttpStatusCode.OK,
            $"login={(int)login.StatusCode}, stats/overview={(int)stats.StatusCode}");
    }
    catch (Exception ex)
    {
        Pass("登录+统计API回归", false, ex.Message);
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
Console.WriteLine("================ M27 前端代码分割 + ECharts 按需引入 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;
