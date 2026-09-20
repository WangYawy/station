using System.Linq.Expressions;

namespace Station.Domain.Repositories;

/// <summary>
/// 分页查询参数（不可变）。所有字段可选。<br/>
/// 用对象承载而非散落参数：以后加"是否去重""是否软删除过滤"等不破坏签名。
/// </summary>
public sealed class PageQuery<T>
{
    /// <summary>过滤条件；null 表示不过滤。</summary>
    public Expression<Func<T, bool>>? Predicate { get; init; }

    /// <summary>页码，从 1 开始；小于 1 会被 clamp。</summary>
    public int PageIndex { get; init; } = 1;

    /// <summary>每页条数；实现层做上下限 clamp。</summary>
    public int PageSize { get; init; } = AppConst.DefaultPageSize;

    /// <summary>
    /// 是否统计总数。<br/>
    /// false 时结果里 <c>Total = -1</c>（"未统计"哨兵值），可省一次 COUNT(*)。<br/>
    /// 大表 + 前端只做"下一页/上一页"不做"跳到第 N 页"时用。
    /// </summary>
    public bool CountTotal { get; init; } = true;
}
