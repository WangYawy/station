using Station.Application.Departments.Commands;
using Station.Application.Users;
using Station.Data.IdGeneration;
using Station.Domain.Departments;
using Station.Domain.Users;

namespace Station.Application.Departments;

public sealed class DeptService : IDeptService
{
    private readonly IDeptRepository _depts;
    private readonly IUserRepository _users;   // 仅用于删除前校验
    private readonly IDeptQueryService _deptQuery;
    private readonly IIdGenerator _idGenerator;

    public DeptService(
        IDeptRepository depts,
        IUserRepository users,
        IDeptQueryService deptQuery,
        IIdGenerator idGenerator)
    {
        _depts = depts;
        _users = users;
        _deptQuery = deptQuery;
        _idGenerator = idGenerator;
    }

    public async Task<long> CreateAsync(
        CreateDeptCommand command, CancellationToken ct = default)
    {
        if (await _deptQuery.ExistsCodeAsync(command.Code, ct))
            throw new InvalidOperationException($"部门编码 {command.Code} 已存在");

        var now = DateTime.Now;
        var dept = Dept.Create(
            _idGenerator.NewId(), command.Code, command.Name,
            command.ParentId, command.SortOrder, now);

        if (!command.IsActive) dept.Deactivate(now);

        await _depts.AddAsync(dept, ct);
        return dept.Id;
    }

    public async Task<OpResult> UpdateAsync(
        UpdateDeptCommand command, CancellationToken ct = default)
    {
        var dept = await _depts.GetByIdAsync(command.Id, ct);
        if (dept is null) return new OpResult(false, "部门不存在");

        var now = DateTime.Now;
        dept.UpdateInfo(command.Name, command.SortOrder, now);
        if (dept.ParentId != command.ParentId) dept.MoveTo(command.ParentId, now);
        if (command.IsActive) dept.Activate(now); else dept.Deactivate(now);

        await _depts.UpdateAsync(dept, ct);
        return new OpResult(true);
    }

    public async Task<OpResult> DeleteAsync(long id, CancellationToken ct = default)
    {
        if (await _depts.HasChildrenAsync(id, ct))
            return new OpResult(false, "存在下级部门，无法删除");

        if (await _users.ExistsActiveInDeptAsync(id, ct))
            return new OpResult(false, "部门下存在用户，无法删除");

        await _depts.DeleteAsync(id, ct);
        return new OpResult(true);
    }
}
