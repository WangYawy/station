namespace Station.Domain.Entities;

/// <summary>部门（组织节点）。部门编码为跨端自然键。</summary>
public sealed class Dept
{
    public long Id { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public long? ParentId { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
