namespace Station.Application.Storage;

/// <summary>多存储目标的结果策略。</summary>
public enum MultiTargetMode
{
    /// <summary>任一 target 成功即认为成功（默认，向后兼容）。</summary>
    AnySuccess = 0,

    /// <summary>全部 target 成功才成功（关键备份场景）。</summary>
    AllRequired = 1,

    /// <summary>主 target（IsPrimary=true）成功即可，其余尽力而为。</summary>
    BestEffort = 2
}
