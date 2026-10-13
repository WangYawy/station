using Station.Domain.Enums;

namespace Station.Application.Users;

public sealed record CreateAccountResult(bool Success, string? Message, long? AccountId = null);

public sealed record OpResult(bool Success, string? Message = null);
