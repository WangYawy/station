using Station.Domain.Enums;

namespace Station.Application.Authorization;

/// <summary>数据范围结果：All 时不限制；否则按 AllowedDeptIds 过滤部门；Self 由调用方按操作人过滤。</summary>
public sealed record DataScopeResult(
    DataScope Scope,
    bool IsAll,
    IReadOnlyCollection<long> AllowedDeptIds,
    long SelfDeptId);

/// <summary>数据权限提供者：根据用户角色计算可见部门集合（含组织树继承）。</summary>
public interface IDataScopeProvider
{
    Task<DataScopeResult> GetDataScopeAsync(long userId);
}
