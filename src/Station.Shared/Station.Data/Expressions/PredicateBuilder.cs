using System.Linq.Expressions;

namespace Station.Data.Expressions;

/// <summary>
/// 表达式树组合工具：把多个 <see cref="Expression{TDelegate}"/> 用 And/Or 拼起来。<br/>
/// 用于动态条件构造，替代 SqlSugar 的 Expressionable，保持上层无 ORM 依赖。
/// </summary>
public static class PredicateBuilder
{
    /// <summary>
    /// 追加一个 AND 条件。<paramref name="left"/> 为 null 时直接返回 <paramref name="right"/>。
    /// </summary>
    public static Expression<Func<T, bool>> And<T>(
        this Expression<Func<T, bool>>? left,
        Expression<Func<T, bool>> right)
    {
        if (left is null) return right;

        var param = Expression.Parameter(typeof(T), "x");
        var body = Expression.AndAlso(
            new ReplaceParameterVisitor(left.Parameters[0], param).Visit(left.Body),
            new ReplaceParameterVisitor(right.Parameters[0], param).Visit(right.Body));

        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    /// <summary>
    /// 追加一个 OR 条件。<paramref name="left"/> 为 null 时直接返回 <paramref name="right"/>。
    /// </summary>
    public static Expression<Func<T, bool>> Or<T>(
        this Expression<Func<T, bool>>? left,
        Expression<Func<T, bool>> right)
    {
        if (left is null) return right;

        var param = Expression.Parameter(typeof(T), "x");
        var body = Expression.OrElse(
            new ReplaceParameterVisitor(left.Parameters[0], param).Visit(left.Body),
            new ReplaceParameterVisitor(right.Parameters[0], param).Visit(right.Body));

        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    /// <summary>把表达式里参数 <paramref name="from"/> 替换成 <paramref name="to"/>。</summary>
    private sealed class ReplaceParameterVisitor(ParameterExpression from, ParameterExpression to)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
