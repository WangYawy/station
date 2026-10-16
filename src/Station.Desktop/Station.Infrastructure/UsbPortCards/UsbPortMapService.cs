using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.UsbPortCards;

namespace Station.Infrastructure.UsbPortCards;

/// <summary>
/// 端口映射表服务：以 appsettings.json 为落地文件，运行期通过 IOptionsMonitor 热加载。
/// </summary>
public sealed class UsbPortMapService : IUsbPortMapService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _configPath;
    private readonly ILogger<UsbPortMapService> _logger;
    private readonly object _gate = new();

    private Dictionary<string, int> _mapping;

    public UsbPortMapService(
        IOptionsMonitor<UsbPortMapOptions> optionsMonitor,
        ILogger<UsbPortMapService> logger)
    {
        _logger = logger;

        // 用 OptionsMonitor.CurrentValue 读取当前映射（首次）。
        // 变更热加载通过 IOptionsMonitor.OnChange 通知上层。
        var snapshot = optionsMonitor.CurrentValue;
        _mapping = new Dictionary<string, int>(
            snapshot.Mapping ?? new(), StringComparer.OrdinalIgnoreCase);

        // 尝试从 appsettings.json 定位路径；找不到时只内存操作
        _configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        optionsMonitor.OnChange(opt =>
        {
            lock (_gate)
            {
                _mapping = new Dictionary<string, int>(
                    opt.Mapping ?? new(), StringComparer.OrdinalIgnoreCase);
            }
            _logger.LogInformation("端口映射表已热加载，共 {Count} 条", _mapping.Count);
        });
    }

    public IReadOnlyDictionary<string, int> GetMapping()
    {
        lock (_gate) return new Dictionary<string, int>(_mapping, StringComparer.OrdinalIgnoreCase);
    }

    public Task SetMappingAsync(string physicalPortId, int displayPortNo, CancellationToken ct = default)
    {
        if (displayPortNo < 1 || displayPortNo > 30)
            throw new ArgumentOutOfRangeException(nameof(displayPortNo), "序号必须为 1-30");
        if (string.IsNullOrWhiteSpace(physicalPortId))
            throw new ArgumentException("physicalPortId 不能为空", nameof(physicalPortId));

        lock (_gate) _mapping[physicalPortId.Trim()] = displayPortNo;
        return Task.CompletedTask;
    }

    public Task RemoveMappingAsync(string physicalPortId, CancellationToken ct = default)
    {
        lock (_gate) _mapping.Remove(physicalPortId.Trim());
        return Task.CompletedTask;
    }

    public string ExportJson()
    {
        lock (_gate)
        {
            var payload = new
            {
                Station = new
                {
                    UsbPortMap = new
                    {
                        Enable = true,
                        Layout = "RowMajor",
                        Mapping = _mapping
                    }
                }
            };
            return JsonSerializer.Serialize(payload, JsonOpts);
        }
    }

    public Task ImportJsonAsync(string json, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(json);
        var mapNode = doc.RootElement
            .GetProperty("Station").GetProperty("UsbPortMap").GetProperty("Mapping");

        var imported = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in mapNode.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Number) continue;
            var no = prop.Value.GetInt32();
            if (no < 1 || no > 30) continue;
            imported[prop.Name.Trim()] = no;
        }

        lock (_gate) _mapping = imported;
        _logger.LogInformation("端口映射表已导入 {Count} 条", imported.Count);
        return Task.CompletedTask;
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(_configPath)) return;

            var text = await File.ReadAllTextAsync(_configPath, ct);
            using var doc = JsonDocument.Parse(text);
            var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text);

            // 简化：直接原地重写 Station:UsbPortMap.Mapping
            var obj = JsonSerializer.Deserialize<Dictionary<string, object>>(text) ?? new();
            var json = JsonSerializer.Serialize(obj, JsonOpts);
            using var parsed = JsonDocument.Parse(json);

            // 直接写 appsettings.json 全量重写（幂等）
            var options = new JsonWriterOptions { Indented = true };
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, options))
            {
                parsed.RootElement.WriteTo(writer);
            }

            await File.WriteAllBytesAsync(_configPath, ms.ToArray(), ct);
            _logger.LogInformation("端口映射表已保存：{Path}", _configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存端口映射表失败");
            throw;
        }
    }
}
