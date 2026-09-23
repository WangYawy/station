namespace Station.Tools.Core.Models;

/// <summary>统一工具执行结果。</summary>
public sealed record ToolResult<T>(bool Success, T? Data, string? ErrorCode, string? ErrorMessage)
{
    public static ToolResult<T> Ok(T data) => new(true, data, null, null);
    public static ToolResult<T> Fail(string code, string message) => new(false, default, code, message);

    public ToolResult<TOut> Map<TOut>(Func<T, TOut> mapper) =>
        Success && Data is not null
            ? ToolResult<TOut>.Ok(mapper(Data))
            : ToolResult<TOut>.Fail(ErrorCode ?? "UNKNOWN", ErrorMessage ?? "未知错误");
}

/// <summary>无数据结果。</summary>
public sealed record ToolResult(bool Success, string? ErrorCode, string? ErrorMessage)
{
    public static ToolResult Ok() => new(true, null, null);
    public static ToolResult Fail(string code, string message) => new(false, code, message);
}
