namespace Station.Application.Accounts.Dtos;

public sealed class AccountOptionDto
{
    public long Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public long? UserId { get; set; }
}
