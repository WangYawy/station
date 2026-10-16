using Station.Domain.Collecting;

namespace Station.Application.Collecting.Dtos;

public sealed record CollectFileDto(
    long Id,
    long TaskId,
    string FileName,
    string? Extension,
    long Size,
    CollectFileStatus Status,
    double Progress,
    double SpeedBytesPerSecond,
    string? ErrorMessage,
    DateTime? CollectedAt,
    string? RemotePath,
    string? UploadError);
