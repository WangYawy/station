namespace Station.Contracts.Commands;

/// <summary>远程指令签名规范化：平台用 SM2 私钥签名，采集站用公钥验签后执行。</summary>
public static class RemoteCommandSignature
{
    public static string Canonical(RemoteCommand command) =>
        $"{command.CommandId}|{command.StationId}|{(int)command.Type}|{command.PayloadJson}|{command.IssuedAt.ToUniversalTime():yyyy-MM-ddTHH:mm:ss}|{command.TimeoutSeconds}";
}
