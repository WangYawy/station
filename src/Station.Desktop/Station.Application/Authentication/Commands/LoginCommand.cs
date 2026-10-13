namespace Station.Application.Authentication.Commands;

/// <summary>登录命令。</summary>
public sealed record LoginCommand(string UserName, string Password);
