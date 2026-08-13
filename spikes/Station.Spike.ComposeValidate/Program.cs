using YamlDotNet.Serialization;

// ---------------------------------------------------------------------------
// M35 Spike: 校验平台 Docker Compose 交付包（YAML 结构 + 环境变量 + DB 切换）
// ---------------------------------------------------------------------------

var deployDir = @"E:\Reny\station\deploy\platform";
var results = new List<(string Step, bool Ok, string Detail)>();
void Pass(string step, bool ok, string detail)
{
    results.Add((step, ok, detail));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

var deserializer = new DeserializerBuilder().Build();

Dictionary<string, object> Load(string file)
{
    var text = File.ReadAllText(Path.Combine(deployDir, file));
    return deserializer.Deserialize<Dictionary<string, object>>(text);
}

// ---------- 1. 三个 Compose 文件 YAML 可解析且服务完整 ----------
try
{
    var mysql = Load("docker-compose.yml");
    var pg = Load("docker-compose.pg.yml");
    var kb = Load("docker-compose.kingbase.yml");
    var mysqlServices = ((Dictionary<object, object>)mysql["services"]).Keys.Cast<string>().ToList();
    var pgServices = ((Dictionary<object, object>)pg["services"]).Keys.Cast<string>().ToList();
    var kbServices = ((Dictionary<object, object>)kb["services"]).Keys.Cast<string>().ToList();
    Pass("Compose结构", mysqlServices.Contains("api") && mysqlServices.Contains("mysql") &&
                         pgServices.Contains("api") && pgServices.Contains("postgres") &&
                         kbServices.Contains("api") && !kbServices.Contains("mysql"),
        $"mysql={string.Join(",", mysqlServices)}, pg={string.Join(",", pgServices)}, kingbase={string.Join(",", kbServices)}");
}
catch (Exception ex)
{
    Pass("Compose结构", false, ex.ToString());
}

// ---------- 2. 三库切换环境变量 ----------
try
{
    string ProviderOf(string file)
    {
        var services = (Dictionary<object, object>)Load(file)["services"];
        var api = (Dictionary<object, object>)services["api"]!;
        var env = (Dictionary<object, object>)api["environment"]!;
        return env["STATION__DB__PROVIDER"]!.ToString()!;
    }

    var mysqlProvider = ProviderOf("docker-compose.yml");
    var pgProvider = ProviderOf("docker-compose.pg.yml");
    var kbProvider = ProviderOf("docker-compose.kingbase.yml");
    Pass("三库切换", mysqlProvider == "MySql" && pgProvider == "PostgreSQL" && kbProvider == "Kingbase",
        $"{mysqlProvider}/{pgProvider}/{kbProvider}");
}
catch (Exception ex)
{
    Pass("三库切换", false, ex.ToString());
}

// ---------- 3. 健康检查与数据卷 ----------
try
{
    var mysql = Load("docker-compose.yml");
    var mysqlService = (Dictionary<object, object>)((Dictionary<object, object>)mysql["services"])["mysql"]!;
    var hasHealth = mysqlService.ContainsKey("healthcheck");
    var hasVolume = mysql.ContainsKey("volumes");
    Pass("健康检查与数据卷", hasHealth && hasVolume, $"healthcheck={hasHealth}, volumes={hasVolume}");
}
catch (Exception ex)
{
    Pass("健康检查与数据卷", false, ex.ToString());
}

// ---------- 4. .env.example 关键项 ----------
try
{
    var env = File.ReadAllLines(Path.Combine(deployDir, ".env.example"))
        .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("#") && l.Contains('='))
        .Select(l => l.Split('=')[0].Trim())
        .ToList();
    var required = new[] { "PLATFORM_HTTP_PORT", "ADMIN_USERNAME", "ADMIN_PASSWORD", "MYSQL_PASSWORD", "PG_PASSWORD", "PLATFORM_COMMAND_PRIVATE_KEY", "PLATFORM_REPORTING_PUBLIC_KEY" };
    Pass(".env模板", required.All(env.Contains), $"共 {env.Count} 项，缺 {required.Except(env).Count()} 项");
}
catch (Exception ex)
{
    Pass(".env模板", false, ex.ToString());
}

// ---------- 5. Dockerfile 关键步骤 ----------
try
{
    var dockerfile = File.ReadAllText(Path.Combine(deployDir, "Dockerfile"));
    Pass("Dockerfile", dockerfile.Contains("FROM mcr.microsoft.com/dotnet/sdk:8.0") &&
                       dockerfile.Contains("dotnet publish src/Station.Platform/Station.Platform.Api") &&
                       dockerfile.Contains("FROM mcr.microsoft.com/dotnet/aspnet:8.0") &&
                       dockerfile.Contains("Station.Platform.Api.dll"),
        "sdk→publish→aspnet 结构完整");
}
catch (Exception ex)
{
    Pass("Dockerfile", false, ex.ToString());
}

Console.WriteLine();
Console.WriteLine("================ M35 Docker Compose 交付包 ================");
var failed = results.Count(r => !r.Ok);
foreach (var (step, ok, detail) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {step}: {detail}");
}

Console.WriteLine($"总计 {results.Count} 项，失败 {failed} 项");
return failed == 0 ? 0 : 1;
