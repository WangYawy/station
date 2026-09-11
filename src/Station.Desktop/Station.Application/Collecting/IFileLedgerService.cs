namespace Station.Application.Collecting;

/// <summary>
/// 采集完成台账服务：为已完成任务生成文件台账（SM3/FileNo/归属），并挂接平台元数据上报。
/// </summary>
public interface IFileLedgerService
{
    Task<int> ProcessCompletedTaskAsync(long taskId);
}
