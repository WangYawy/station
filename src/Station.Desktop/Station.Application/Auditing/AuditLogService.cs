using Microsoft.Extensions.Logging;
using Station.Application.Auditing.Dtos;
using Station.Application.Session;
using Station.Application.Users;
using Station.Application.Users.Dtos;
using Station.Data.IdGeneration;
using Station.Domain.Auditing;

namespace Station.Application.Auditing;

public sealed class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _repository;
    private readonly IIdGenerator _idGenerator;
    private readonly IUserQueryService _userQuery;
    private readonly ISessionManager _sessions;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(
        IAuditLogRepository repository,
        IIdGenerator idGenerator,
        IUserQueryService userQuery,
        ISessionManager sessions,
        ILogger<AuditLogService> logger)
    {
        _repository = repository;
        _idGenerator = idGenerator;
        _userQuery = userQuery;
        _sessions = sessions;
        _logger = logger;
    }

    // =========================================================
    // 写
    // =========================================================

    public async Task WriteAsync(AuditLog entry, CancellationToken ct = default)
    {
        if (entry.CreatedAt == default)
            entry.CreatedAt = DateTime.Now;

        if (entry.Id == 0)
            entry.Id = _idGenerator.NewId();

        await _repository.AddAsync(entry, ct);
    }

    // =========================================================
    // 词表（零 IO）
    // =========================================================

    public IReadOnlyList<AuditOperationTypeDescriptor> GetOperationTypes()
        => AuditOperationTypes.All;

    // =========================================================
    // 最近日志
    // =========================================================

    public async Task<List<AuditLog>> GetRecentAsync(
        IEnumerable<string>? operationTypes = null,
        int take = 20,
        CancellationToken ct = default)
    {
        var types = operationTypes?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var list = await _repository.GetRecentAsync(types, take, ct);
        return list.ToList();
    }

    // =========================================================
    // 操作人目录
    // =========================================================

    public async Task<IReadOnlyList<OperatorOptionDto>> GetSelectableOperatorsAsync(
        CancellationToken ct = default)
    {
        var current = _sessions.Current;
        if (current is null) return [];

        var users = await _userQuery.PageAsync(new UserPageQuery { PageSize = 200 }, ct);

        var isAdmin = current.Roles?.Any(r =>
            string.Equals(r, "admin", StringComparison.OrdinalIgnoreCase)) == true;

        var filtered = isAdmin
            ? users.Items
            : users.Items.Where(u => string.Equals(
                u.UserNo, current.UserName, StringComparison.OrdinalIgnoreCase));

        return filtered
            .OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .Select(u => new OperatorOptionDto(u.UserNo, u.Name))
            .ToList();
    }

    // =========================================================
    // 归档（保留占位）
    // =========================================================

    public Task<int> ArchiveAsync(int retentionDays, CancellationToken ct = default)
        => Task.FromResult(0);
}
