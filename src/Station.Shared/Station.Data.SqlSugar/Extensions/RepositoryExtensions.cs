using SqlSugar;
using Station.Data.Repositories;
using Station.Data.SqlSugar.Repositories;

namespace Station.Data.SqlSugar.Extensions;

/// <summary>
/// SqlSugar 逃生舱扩展方法。<br/>
/// <b>调用它意味着你所在的层主动依赖了 ORM，请在 Code Review 时说明原因。</b><br/>
/// Application 层原则禁止 <c>using Station.Data.SqlSugar.Extensions;</c>；<br/>
/// 复杂查询推荐在 Infrastructure 层编写专用查询服务（接口定义在 Application）。
/// </summary>
public static class RepositoryExtensions
{
    /// <summary>
    /// 获取底层 <see cref="ISugarQueryable{T}"/>，用于 SqlSugar 特有查询。<br/>
    /// 仅支持 <see cref="RepositoryBase{T}"/> 实现（SqlSugar 提供）。
    /// </summary>
    public static ISugarQueryable<T> AsQueryable<T>(this IRepository<T> repo)
        where T : class, new()
    {
        if (repo is RepositoryBase<T> impl)
        {
            return impl.DbContext.Queryable<T>();
        }

        throw new InvalidOperationException(
            $"AsQueryable 仅支持由 Station.Data.SqlSugar 提供的 {nameof(RepositoryBase<T>)} 实现；" +
            $"当前实现为 {repo.GetType().FullName}。");
    }
}
