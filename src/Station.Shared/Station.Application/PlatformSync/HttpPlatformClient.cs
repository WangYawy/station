using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Station.Contracts.Alerts;
using Station.Contracts.Api;
using Station.Contracts.Commands;
using Station.Contracts.Registration;
using Station.Contracts.Reporting;
using Station.Contracts.Sync;
using Microsoft.Extensions.Logging;

namespace Station.Application.PlatformSync;

public sealed class HttpPlatformClient : IPlatformClient
{
    private readonly HttpClient _http;
    private readonly PlatformOptions _options;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly ILogger<HttpPlatformClient> _logger;

    public HttpPlatformClient(HttpClient http, IOptions<PlatformOptions> options, ILogger<HttpPlatformClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        }
    }

    public async Task<StationRegistrationResponse?> RegisterAsync(
        StationRegistrationRequest request,
        CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(ApiRoutes.RegisterStation, request, _json, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ApiResponse<StationRegistrationResponse>>(_json, ct)
            .ContinueWith(t => t.Result?.Data, ct);
    }

    public async Task<ReportResult?> ReportMetadataAsync(FileMetadataReport report, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(
            ApiRoutes.ReportFileMetadata.Replace("{stationId}", report.StationId.ToString()),
            report, _json, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ApiResponse<ReportResult>>(_json, ct)
            .ContinueWith(t => t.Result?.Data, ct);
    }

    public async Task<bool> ReportAlertAsync(AlertReport report, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(
            ApiRoutes.ReportAlert.Replace("{stationId}", report.StationId.ToString()),
            report, _json, ct);
        response.EnsureSuccessStatusCode();
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ReportLicenseStatusAsync(Station.Contracts.Reporting.LicenseStatusReport report, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(
            $"{ApiRoutes.Base}/stations/{report.StationId}/license",
            report, _json, ct);
        response.EnsureSuccessStatusCode();
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ReportEmergencyTasksAsync(
        long stationId,
        IReadOnlyList<EmergencyTaskItem> tasks,
        CancellationToken ct)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                ApiRoutes.ReportEmergencyTasks.Replace("{stationId}", stationId.ToString()),
                new EmergencyTaskReport(stationId, tasks),
                _json, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            _logger.LogWarning("紧急任务上报失败（平台不可达），下轮重试：{BaseUrl}", _options.BaseUrl);
            return false; // 平台不可达等下轮同步重试
        }
    }

    public async Task<ConfigSyncResponse?> SyncConfigAsync(ConfigSyncRequest request, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(
            ApiRoutes.SyncConfig.Replace("{stationId}", request.StationId.ToString()),
            request, _json, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ApiResponse<ConfigSyncResponse>>(_json, ct)
            .ContinueWith(t => t.Result?.Data, ct);
    }

    public async Task<IReadOnlyList<RemoteCommand>> PollCommandsAsync(long stationId, CancellationToken ct)
    {
        var response = await _http.GetAsync(
            ApiRoutes.PollCommands.Replace("{stationId}", stationId.ToString()),
            ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<RemoteCommand>>>(_json, ct);
        return body?.Data ?? [];
    }

    public async Task<bool> ReportCommandResultAsync(long stationId, CommandExecutionResult result, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(
            ApiRoutes.ReportCommandResult
                .Replace("{stationId}", stationId.ToString())
                .Replace("{commandId}", result.CommandId.ToString()),
            result, _json, ct);
        response.EnsureSuccessStatusCode();
        return response.IsSuccessStatusCode;
    }
}
