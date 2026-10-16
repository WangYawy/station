using Station.Contracts;

namespace Station.Application.Alerts.Dtos;
public sealed record AlertDto(
    long Id,
    AlertType Type,
    AlertLevel Level,
    AlertStatus Status,
    string Title,
    string? Detail,
    string? Source,
    DateTime CreatedAt);
