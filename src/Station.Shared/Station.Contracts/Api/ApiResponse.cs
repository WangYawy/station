namespace Station.Contracts.Api;

/// <summary>统一 API 响应包装。</summary>
public sealed record ApiResponse<T>(bool Success, int Code, string Message, T? Data)
{
    public static ApiResponse<T> Ok(T data) => new(true, 0, "ok", data);

    public static ApiResponse<T> Fail(int code, string message) => new(false, code, message, default);
}
