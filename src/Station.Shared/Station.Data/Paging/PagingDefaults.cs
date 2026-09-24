namespace Station.Data.Paging;

/// <summary>分页的编译期默认值（Options 未配置时的兜底）。</summary>
public static class PagingDefaults
{
    /// <summary>默认每页条数。</summary>
    public const int DefaultPageSize = 20;

    /// <summary>每页条数上限（防止恶意请求）。</summary>
    public const int MaxPageSize = 200;
}
