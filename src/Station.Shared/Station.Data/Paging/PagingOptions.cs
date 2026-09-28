namespace Station.Data.Paging;

/// <summary>分页配置，对应配置节 <c>Station:Data:Paging</c>。</summary>
public sealed class PagingOptions
{
    /// <summary>默认配置节名。</summary>
    public const string SectionName = "Data:Paging";

    /// <summary>默认每页条数。</summary>
    public int DefaultPageSize { get; set; } = PagingDefaults.DefaultPageSize;

    /// <summary>每页条数上限。</summary>
    public int MaxPageSize { get; set; } = PagingDefaults.MaxPageSize;
}
