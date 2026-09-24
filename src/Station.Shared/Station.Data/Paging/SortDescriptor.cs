using System.Linq.Expressions;

namespace Station.Data.Paging;

/// <summary>
/// 排序描述符：把 <c>Expression&lt;Func&lt;T, TKey&gt;&gt;</c> 擦除为
/// <c>Expression&lt;Func&lt;T, object&gt;&gt;</c>，以便多个不同字段组成"多级排序"。<br/>
/// 通过 <see cref="Expression.Convert"/> 构造，SqlSugar / EF 会剥掉 Convert 拿到真实成员。
/// </summary>
public sealed class SortDescriptor<T>
{
    /// <summary>排序字段选择器。</summary>
    public required Expression<Func<T, object>> KeySelector { get; init; }

    /// <summary>是否倒序。</summary>
    public bool Descending { get; init; }

    /// <summary>构造正序排序描述符。</summary>
    public static SortDescriptor<T> Asc<TKey>(Expression<Func<T, TKey>> selector)
        => new() { KeySelector = ToObjectSelector(selector), Descending = false };

    /// <summary>构造倒序排序描述符。</summary>
    public static SortDescriptor<T> Desc<TKey>(Expression<Func<T, TKey>> selector)
        => new() { KeySelector = ToObjectSelector(selector), Descending = true };

    private static Expression<Func<T, object>> ToObjectSelector<TKey>(
        Expression<Func<T, TKey>> selector)
    {
        var param = selector.Parameters[0];
        var body = Expression.Convert(selector.Body, typeof(object));
        return Expression.Lambda<Func<T, object>>(body, param);
    }
}
