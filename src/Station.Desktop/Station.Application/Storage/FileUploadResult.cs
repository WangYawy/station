namespace Station.Application.Storage;

public sealed record FileUploadResult(
    bool Success,
    string? RemotePath,
    string? ErrorMessage,
    int RetryCount,
    bool CircuitBreakerOpen);
