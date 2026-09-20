using System.Linq.Expressions;

namespace Station.Domain.Repositories;

/// <summary>
/// 排序描述符：把 <c>Expression<Func<T, TKey>></T></c> 擦除为
/// <c>Expression<Func<T, object>></T></c>，以便多个不同字段组成"多级排序"。<br/>
/// 通过 <see cref="Expression.Convert"/> 构造，SqlSugar / EF 会剥掉 Convert 拿到真实成员。
/// </summary>
public sealed class SortDescriptor<T>
{
    /// <summary>
    /// 排序字段
    /// </summary>
    public required Expression<Func<T, object>> KeySelector { get; init; }
    /// <summary>
    /// 是否倒序
    /// </summary>
    public bool Descending { get; init; }

    /// <summary>
    /// 正序
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <param name="selector"></param>
    /// <returns></returns>
    public static SortDescriptor<T> Asc<TKey>(Expression<Func<T, TKey>> selector)
        => new() { KeySelector = ToObjectSelector(selector), Descending = false };

    /// <summary>
    /// 倒序
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <param name="selector"></param>
    /// <returns></returns>
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
