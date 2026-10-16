using Station.Domain.Collecting;

namespace Station.Application.Collecting.Dtos;

public sealed class CollectFilePageQuery
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Keyword { get; set; }

    public string? Extension { get; set; }
    public CollectFileStatus? Status { get; set; }
    public UploadStatus? SyncStatus { get; set; }

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
