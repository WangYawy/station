using Microsoft.Extensions.Logging;
using Station.Application.Collecting;
using Station.Data.Repositories;
using Station.Data.SqlSugar.Extensions;

namespace Station.Infrastructure.Collecting;
public sealed class CollectFileNoGenerator : ICollectFileNoGenerator
{
    private readonly ICollectFileQueryService _fileQuery;
    private readonly ILogger<CollectFileNoGenerator> _logger;

    public CollectFileNoGenerator(ICollectFileQueryService fileQuery, ILogger<CollectFileNoGenerator> logger)
    {
        _fileQuery = fileQuery;
        _logger = logger;
    }

    public async Task<IFileNoContext> CreateContextAsync(string taskNo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskNo))
            throw new ArgumentException("TaskNo 不能为空。", nameof(taskNo));

        // 查询该任务下已有最大文件编号，用于中断恢复
        var maxFileNo = await _fileQuery.GetMaxFileNoByTaskIdAsync(taskNo)
            .ConfigureAwait(false);

        var startSeq = 0;
        if (!string.IsNullOrEmpty(maxFileNo))
        {
            var lastDash = maxFileNo.LastIndexOf('-');
            if (lastDash >= 0 && int.TryParse(maxFileNo[(lastDash + 1)..], out var seq))
                startSeq = seq;
        }

        _logger.LogInformation("任务 {TaskNo} 文件编号上下文已创建，起始序号 {StartSeq}。",
            taskNo, startSeq + 1);

        return new FileNoContext(taskNo, startSeq);
    }

    private sealed class FileNoContext : IFileNoContext
    {
        private readonly string _taskNo;
        private int _seq;

        public FileNoContext(string taskNo, int startSeq)
        {
            _taskNo = taskNo;
            _seq = startSeq;
        }

        public int CurrentSeq => Volatile.Read(ref _seq);

        public string Next()
        {
            var seq = Interlocked.Increment(ref _seq);
            return $"{_taskNo}-{seq:D6}";
        }

        public void Dispose()
        {
            // 无资源需要释放，保留以便未来扩展（如埋点上报）
        }
    }
}
