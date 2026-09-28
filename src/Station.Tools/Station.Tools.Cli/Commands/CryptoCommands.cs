using Spectre.Console;
using Spectre.Console.Cli;
using Station.Crypto.Engine.Keys;
using Station.Tools.Cli.Commands.Shared;
using Station.Tools.Core.Crypto;

namespace Station.Tools.Cli.Commands;

// ============ 摘要 ============

public sealed class CryptoHashCommand : AsyncCommand<CryptoHashCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-t|--text <TEXT>")] public string? Text { get; init; }
        [CommandOption("-i|--in <PATH>")] public string? Input { get; init; }
        [CommandOption("-a|--algo <ALGO>")] public string Algorithm { get; init; } = "SM3";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        Station.Tools.Core.Models.ToolResult<string> result;

        if (!string.IsNullOrEmpty(settings.Text))
            result = HashTool.Hash(settings.Text, settings.Algorithm);
        else if (!string.IsNullOrEmpty(settings.Input) && File.Exists(settings.Input))
            result = await HashTool.HashFileAsync(settings.Input, settings.Algorithm);
        else
        {
            IoHelpers.ShowError("NO_INPUT", "请用 -t 或 -i 指定输入");
            return 1;
        }

        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        AnsiConsole.WriteLine(result.Data!);
        return 0;
    }
}

// ============ 加密 ============

public sealed class CryptoEncryptCommand : AsyncCommand<CryptoEncryptCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-t|--text <TEXT>")] public string? Text { get; init; }
        [CommandOption("-m|--master <PATH>")] public string? MasterKeyFile { get; init; }
        [CommandOption("-a|--algo <ALGO>")] public string Algorithm { get; init; } = "SM4-GCM";
        [CommandOption("--aad <AAD>")] public string? Aad { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrEmpty(settings.Text) || string.IsNullOrEmpty(settings.MasterKeyFile))
        {
            IoHelpers.ShowError("NO_INPUT", "需要 --text 和 --master");
            return 1;
        }

        var keyFile = MasterKeyFileDto.FromJson(await File.ReadAllTextAsync(settings.MasterKeyFile));
        var result = EncryptTool.Encrypt(settings.Text, keyFile, settings.Algorithm, settings.Aad);

        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        AnsiConsole.WriteLine(result.Data!);
        return 0;
    }
}

// ============ 解密 ============

public sealed class CryptoDecryptCommand : AsyncCommand<CryptoDecryptCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-t|--text <TEXT>")] public string? Text { get; init; }
        [CommandOption("-i|--in <PATH>")] public string? Input { get; init; }
        [CommandOption("-m|--master <PATH>")] public string? MasterKeyFile { get; init; }
        [CommandOption("-a|--algo <ALGO>")] public string Algorithm { get; init; } = "SM4-GCM";
        [CommandOption("--aad <AAD>")] public string? Aad { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var cipher = settings.Text;
        if (string.IsNullOrEmpty(cipher) && !string.IsNullOrEmpty(settings.Input) && File.Exists(settings.Input))
            cipher = (await File.ReadAllTextAsync(settings.Input)).Trim();

        if (string.IsNullOrEmpty(cipher) || string.IsNullOrEmpty(settings.MasterKeyFile))
        {
            IoHelpers.ShowError("NO_INPUT", "需要密文（--text 或 -i）和 --master");
            return 1;
        }

        var keyFile = MasterKeyFileDto.FromJson(await File.ReadAllTextAsync(settings.MasterKeyFile));
        var result = EncryptTool.Decrypt(cipher, keyFile, settings.Algorithm, settings.Aad);

        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        AnsiConsole.WriteLine(result.Data!);
        return 0;
    }
}

// ============ 签名 ============

public sealed class CryptoSignCommand : AsyncCommand<CryptoSignCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-t|--text <TEXT>")] public string? Text { get; init; }
        [CommandOption("-i|--in <PATH>")] public string? Input { get; init; }
        [CommandOption("-k|--key <PATH>")] public string? PrivateKey { get; init; }
        [CommandOption("-a|--algo <ALGO>")] public string Algorithm { get; init; } = "SM2-SM3";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrEmpty(settings.PrivateKey) || !File.Exists(settings.PrivateKey))
        {
            IoHelpers.ShowError("NO_KEY", "请用 -k 指定私钥文件");
            return 1;
        }

        var keyPem = await File.ReadAllTextAsync(settings.PrivateKey);
        Station.Tools.Core.Models.ToolResult<string> result;

        if (!string.IsNullOrEmpty(settings.Text))
            result = SignTool.Sign(settings.Text, keyPem, settings.Algorithm);
        else if (!string.IsNullOrEmpty(settings.Input) && File.Exists(settings.Input))
            result = await SignTool.SignFileAsync(settings.Input, keyPem, settings.Algorithm);
        else
        {
            IoHelpers.ShowError("NO_INPUT", "请用 -t 或 -i 指定输入");
            return 1;
        }

        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        AnsiConsole.WriteLine(result.Data!);
        return 0;
    }
}

// ============ 验签 ============

public sealed class CryptoVerifyCommand : AsyncCommand<CryptoVerifyCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-t|--text <TEXT>")] public string? Text { get; init; }
        [CommandOption("-s|--signature <SIG>")] public string? Signature { get; init; }
        [CommandOption("-k|--key <PATH>")] public string? PublicKey { get; init; }
        [CommandOption("-a|--algo <ALGO>")] public string Algorithm { get; init; } = "SM2-SM3";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrEmpty(settings.Signature) ||
            string.IsNullOrEmpty(settings.PublicKey) || !File.Exists(settings.PublicKey))
        {
            IoHelpers.ShowError("NO_INPUT", "需要 --signature 和 -k");
            return 1;
        }

        var keyPem = await File.ReadAllTextAsync(settings.PublicKey);
        var result = SignTool.Verify(settings.Text ?? string.Empty, settings.Signature, keyPem, settings.Algorithm);

        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        if (result.Data)
        {
            IoHelpers.ShowSuccess("签名有效");
            return 0;
        }

        AnsiConsole.MarkupLine("[red]✗ 签名无效[/]");
        return 2;
    }
}

// ============ STFE 加密 ============

public sealed class StfeEncryptCommand : AsyncCommand<StfeEncryptCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-i|--in <PATH>")] public string? Input { get; init; }
        [CommandOption("-m|--master <PATH>")] public string? MasterKeyFile { get; init; }
        [CommandOption("-a|--algo <ALGO>")] public string Algorithm { get; init; } = "SM4-GCM";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrEmpty(settings.Input) || !File.Exists(settings.Input) ||
            string.IsNullOrEmpty(settings.MasterKeyFile) || !File.Exists(settings.MasterKeyFile))
        {
            IoHelpers.ShowError("NO_INPUT", "需要 -i 和 --master");
            return 1;
        }

        var output = settings.Output ?? settings.Input + ".stfe";
        var dto = Station.Crypto.Engine.Keys.MasterKeyFileDto.FromJson(
            await File.ReadAllTextAsync(settings.MasterKeyFile));

        var result = await StfeFileTool.EncryptAsync(
            settings.Input, output, dto, settings.Algorithm);

        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        IoHelpers.ShowSuccess($"加密完成：{output}（{result.Data} 字节）");
        return 0;
    }
}

// ============ STFE 解密 ============

public sealed class StfeDecryptCommand : AsyncCommand<StfeDecryptCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-i|--in <PATH>")] public string? Input { get; init; }
        [CommandOption("-m|--master <PATH>")] public string? MasterKeyFile { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrEmpty(settings.Input) || !File.Exists(settings.Input) ||
            string.IsNullOrEmpty(settings.MasterKeyFile) || !File.Exists(settings.MasterKeyFile))
        {
            IoHelpers.ShowError("NO_INPUT", "需要 -i 和 --master");
            return 1;
        }

        var output = settings.Output ?? settings.Input.Replace(".stfe", "");
        var dto = Station.Crypto.Engine.Keys.MasterKeyFileDto.FromJson(
            await File.ReadAllTextAsync(settings.MasterKeyFile));

        var result = await StfeFileTool.DecryptAsync(settings.Input, output, dto);

        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        IoHelpers.ShowSuccess($"解密完成：{output}（{result.Data} 字节）");
        return 0;
    }
}

// ============ STFE 检查 ============

public sealed class StfeInspectCommand : Command<StfeInspectCommand.Settings>
{
    public sealed class Settings : SettingsBase
    {
        [CommandOption("-i|--in <PATH>")] public string? Input { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (string.IsNullOrEmpty(settings.Input) || !File.Exists(settings.Input))
        {
            IoHelpers.ShowError("NO_INPUT", "请用 -i 指定文件");
            return 1;
        }

        var result = StfeFileTool.Inspect(settings.Input);
        if (!result.Success)
        {
            IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage);
            return 1;
        }

        var h = result.Data!;
        var table = new Table().Border(TableBorder.Rounded).AddColumn("属性").AddColumn("值");
        table.AddRow("格式版本", h.Version.ToString());
        table.AddRow("算法", h.AlgorithmName);
        table.AddRow("密钥版本", $"v{h.KeyVersion}");
        table.AddRow("分块大小", $"{h.ChunkSize / 1024 / 1024} MB");
        table.AddRow("密文总大小", $"{h.TotalCiphertextSize:N0} 字节");
        AnsiConsole.Write(table);
        return 0;
    }
}
