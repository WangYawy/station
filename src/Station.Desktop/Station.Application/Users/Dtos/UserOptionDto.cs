namespace Station.Application.Users.Dtos;

public sealed class UserOptionDto
{
    public long Id { get; set; }
    public string UserNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long DeptId { get; set; }
}
