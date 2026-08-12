namespace Station.Contracts.Api;

/// <summary>站↔平台通信 API 路由（版本化 /api/v1，只增不改不删）。</summary>
public static class ApiRoutes
{
    public const string Version = "v1";
    public const string Base = "api/v1";

    public const string RegisterStation = Base + "/stations/register";
    public const string SyncConfig = Base + "/stations/{stationId}/config-sync";
    public const string ReportFileMetadata = Base + "/stations/{stationId}/files/metadata";
    public const string ReportAlert = Base + "/stations/{stationId}/alerts";
    public const string PollCommands = Base + "/stations/{stationId}/commands/poll";
    public const string ReportCommandResult = Base + "/stations/{stationId}/commands/{commandId}/result";
}
