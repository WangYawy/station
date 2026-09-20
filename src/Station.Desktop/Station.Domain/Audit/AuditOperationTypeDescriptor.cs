namespace Station.Domain.Audit;

/// <summary>
/// 审计操作类型描述符：受控词表的一条。<br/>
/// 存储用 <see cref="Code"/>（稳定不变），界面显示 <see cref="DisplayName"/>。
/// </summary>
/// <param name="Code">存储值，全小写点分，如 login / backup.auto。</param>
/// <param name="DisplayName">界面显示名。</param>
/// <param name="Category">分组，用于下拉分组或后续筛选扩展。</param>
/// <param name="SortOrder">排序权重，越小越靠前。</param>
public sealed record AuditOperationTypeDescriptor(
    string Code,
    string DisplayName,
    string Category,
    int SortOrder);
