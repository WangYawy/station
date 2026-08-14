using System.Text.Json;
using System.Text.Json.Nodes;
using Station.Application.Authentication;
using Station.Application.Settings;

namespace Station.Desktop.WebHost.Settings;

/// <summary>
/// 网络安全设置实现：WebOptions 来自配置（含运行时覆盖），登录锁定热应用到 AuthOptions 单例，
/// Web 端口/HTTPS/局域网调整写回运行时文件并提示重启生效；证书保存到本地数据目录。
/// </summary>
public sealed class NetworkSettingsService : INetworkSettingsService
{
    private readonly WebOptions _web;
    private readonly AuthOptions _auth;
    private readonly IRuntimeSettingsFile _runtimeFile;

    private static string CertificateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Station", "certificates");

    public NetworkSettingsService(WebOptions web, AuthOptions auth, IRuntimeSettingsFile runtimeFile)
    {
        _web = web;
        _auth = auth;
        _runtimeFile = runtimeFile;
    }

    public NetworkSettingsDto Get()
    {
        var status = "未安装";
        if (!string.IsNullOrWhiteSpace(_web.CertificatePath) && File.Exists(_web.CertificatePath))
        {
            try
            {
                using var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(_web.CertificatePath, _web.CertificatePassword);
                status = $"已安装（有效期至 {cert.NotAfter:yyyy-MM-dd}）";
            }
            catch
            {
                status = "已安装但证书不可用（密码/格式错误）";
            }
        }

        return new NetworkSettingsDto(
            _web.Port,
            _web.HttpsPort,
            _web.EnableLan,
            _web.EnableHttps,
            _web.AllowHttp,
            status,
            _auth.MaxFailedAttempts,
            _auth.LockoutMinutes);
    }

    public IReadOnlyList<string> Update(IReadOnlyDictionary<string, string> values)
    {
        var hints = new List<string>();
        var webChanged = false;

        if (values.TryGetValue("webPort", out var webPort) && int.TryParse(webPort, out var port) && port is > 0 and < 65536)
        {
            if (port != _web.Port) webChanged = true;
            _web.Port = port;
        }

        if (values.TryGetValue("httpsPort", out var httpsPort) && int.TryParse(httpsPort, out var hport) && hport is > 0 and < 65536)
        {
            if (hport != _web.HttpsPort) webChanged = true;
            _web.HttpsPort = hport;
        }

        if (values.TryGetValue("enableLan", out var lan))
        {
            var parsed = bool.TryParse(lan, out var p) && p;
            if (parsed != _web.EnableLan) webChanged = true;
            _web.EnableLan = parsed;
        }

        if (values.TryGetValue("enableHttps", out var https))
        {
            var parsed = bool.TryParse(https, out var p) && p;
            if (parsed != _web.EnableHttps) webChanged = true;
            _web.EnableHttps = parsed;
        }

        if (values.TryGetValue("allowHttp", out var allowHttp))
        {
            var parsed = bool.TryParse(allowHttp, out var p) && p;
            if (parsed != _web.AllowHttp) webChanged = true;
            _web.AllowHttp = parsed;
        }

        if (values.TryGetValue("maxFailedAttempts", out var attempts) && int.TryParse(attempts, out var ma))
        {
            _auth.MaxFailedAttempts = Math.Max(1, ma);
        }

        if (values.TryGetValue("lockoutMinutes", out var lockout) && int.TryParse(lockout, out var lm))
        {
            _auth.LockoutMinutes = Math.Max(1, lm);
        }

        if (webChanged)
        {
            hints.Add("Web 端口/局域网/HTTPS 调整需重启后生效");
        }

        PersistRuntime();
        return hints;
    }

    public (bool Ok, string Message) UploadCertificate(string fileName, string base64Content, string password)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64Content);
        }
        catch (Exception ex)
        {
            return (false, $"证书内容不是有效 Base64：{ex.Message}");
        }

        try
        {
            Directory.CreateDirectory(CertificateDirectory);
            var safeName = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "station.pfx" : fileName);
            var targetPath = Path.Combine(CertificateDirectory, safeName);
            File.WriteAllBytes(targetPath, bytes);

            using var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(targetPath, password);
            _web.CertificatePath = targetPath;
            _web.CertificatePassword = password;
            _web.EnableHttps = true;
            _web.EnableLan = true;
            PersistRuntime();
            return (true, $"证书已安装（{cert.Subject}，有效期至 {cert.NotAfter:yyyy-MM-dd}）");
        }
        catch (Exception ex)
        {
            return (false, $"证书安装失败：{ex.Message}");
        }
    }

    private void PersistRuntime()
    {
        var root = new JsonObject();
        try
        {
            var json = _runtimeFile.ReadJson();
            if (!string.IsNullOrWhiteSpace(json))
            {
                root = (JsonNode.Parse(json) as JsonObject) ?? new JsonObject();
            }
        }
        catch
        {
            // 忽略损坏的运行时文件，重新生成
        }

        var station = root["Station"] as JsonObject ?? new JsonObject();
        station["Web"] = JsonSerializer.SerializeToNode(new
        {
            _web.Port,
            _web.EnableLan,
            _web.EnableHttps,
            _web.HttpsPort,
            _web.AllowHttp,
            _web.CertificatePath,
            _web.CertificatePassword
        });
        station["Auth"] = JsonSerializer.SerializeToNode(new
        {
            _auth.MaxFailedAttempts,
            _auth.LockoutMinutes
        });
        root["Station"] = station;
        _runtimeFile.WriteJson(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
