using Spectre.Console;
using Station.Tools.Core.Binding;
using Station.Tools.Core.Crypto;
using Station.Tools.Core.Inspection;
using Station.Tools.Core.Keys;
using Station.Tools.Core.Licensing;
using Station.Tools.Core.Models;
using Station.Crypto.KeyGen;
using Station.Tools.Cli.Commands.Shared;

namespace Station.Tools.Cli.Interactive;

/// <summary>交互式菜单。</summary>
public sealed class InteractiveMenu
{
    public async Task<int> RunAsync()
    {
        while (true)
        {
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]请选择功能：[/]")
                    .PageSize(15)
                    .AddChoices(
                        "🔑 密钥生成 - SM2",
                        "🔑 密钥生成 - RSA",
                        "🔑 密钥生成 - 主密钥",
                        "🔍 密钥检查",
                        "🔐 摘要计算",
                        "🔐 加密",
                        "🔐 解密",
                        "✍️  签名",
                        "✍️  验签",
                        "📜 生成授权文件",
                        "📜 验证授权文件",
                        "📜 查看授权内容",
                        "🔗 生成绑定文件",
                        "🔗 验证绑定文件",
                        "🔍 查询审计日志",
                        "🔍 查询加密策略",
                        "🔍 检查表结构",
                        "── 退出 ──"));

            if (choice.StartsWith("──")) return 0;

            try
            {
                await HandleAsync(choice);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(ex.Message)}[/]");
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]按任意键返回菜单...[/]");
            Console.ReadKey(true);
        }
    }

    private async Task HandleAsync(string choice)
    {
        switch (choice)
        {
            case "🔑 密钥生成 - SM2": await GenSm2Async(); break;
            case "🔑 密钥生成 - RSA": await GenRsaAsync(); break;
            case "🔑 密钥生成 - 主密钥": await GenMasterAsync(); break;
            case "🔍 密钥检查": await InspectKeyAsync(); break;
            case "🔐 摘要计算": await HashAsync(); break;
            case "🔐 加密": await EncryptAsync(); break;
            case "🔐 解密": await DecryptAsync(); break;
            case "✍️  签名": await SignAsync(); break;
            case "✍️  验签": await VerifyAsync(); break;
            case "📜 生成授权文件": await GenLicenseAsync(); break;
            case "📜 验证授权文件": await VerifyLicenseAsync(); break;
            case "📜 查看授权内容": await InspectLicenseAsync(); break;
            case "🔗 生成绑定文件": await GenBindingAsync(); break;
            case "🔗 验证绑定文件": await VerifyBindingAsync(); break;
            case "🔍 查询审计日志": await QueryAuditAsync(); break;
            case "🔍 查询加密策略": await QueryPolicyAsync(); break;
            case "🔍 检查表结构": await CheckSchemaAsync(); break;
        }
    }

    // ============================================================
    // 各功能实现
    // ============================================================

    private async Task GenSm2Async()
    {
        var name = AnsiConsole.Ask("密钥名（用于文件名）:", "license");
        var outDir = AnsiConsole.Ask("输出目录:", ".");

        var result = Sm2KeyTool.Generate();
        if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return; }

        var privPath = Path.Combine(outDir, $"{name}-private.pem");
        var pubPath = Path.Combine(outDir, $"{name}-public.pem");
        await File.WriteAllTextAsync(privPath, result.Data!.PrivatePem);
        await File.WriteAllTextAsync(pubPath, result.Data.PublicPem);

        IoHelpers.ShowSuccess($"已生成：\n  私钥 {privPath}\n  公钥 {pubPath}");
        AnsiConsole.MarkupLine("[yellow]⚠ 私钥必须严格保密[/]");
    }

    private async Task GenRsaAsync()
    {
        var bits = AnsiConsole.Prompt(new SelectionPrompt<int>()
            .Title("位数：").AddChoices(2048, 3072, 4096));
        var outDir = AnsiConsole.Ask("输出目录:", ".");
        var name = AnsiConsole.Ask("密钥名:", "rsa");

        var result = RsaKeyTool.Generate(bits);
        if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return; }

        await File.WriteAllTextAsync(Path.Combine(outDir, $"{name}-private.pem"), result.Data!.PrivatePem);
        await File.WriteAllTextAsync(Path.Combine(outDir, $"{name}-public.pem"), result.Data.PublicPem);
        IoHelpers.ShowSuccess($"RSA-{bits} 密钥对已生成");
    }

    private async Task GenMasterAsync()
    {
        var count = AnsiConsole.Ask("生成密钥版本数量:", 1);
        var path = AnsiConsole.Ask("输出路径:", "master.key");

        var result = MasterKeyTool.Generate(count);
        if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return; }

        await File.WriteAllTextAsync(path, result.Data!);
        IoHelpers.ShowSuccess($"主密钥文件已生成：{path}");
    }

    private Task InspectKeyAsync()
    {
        var path = AnsiConsole.Ask<string>("PEM 文件路径:");
        if (!File.Exists(path)) { IoHelpers.ShowError("FILE_NOT_FOUND", "文件不存在"); return Task.CompletedTask; }

        var pem = File.ReadAllText(path);
        var result = KeyInspectTool.Inspect(pem);
        if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return Task.CompletedTask; }

        var info = result.Data!;
        var table = new Table().Border(TableBorder.Rounded).AddColumn("属性").AddColumn("值");
        table.AddRow("类型", info.Type);
        table.AddRow("种类", info.Kind);
        table.AddRow("位数", info.Bits.ToString());
        table.AddRow("SM3 指纹", info.Fingerprint);
        AnsiConsole.Write(table);
        return Task.CompletedTask;
    }

    private async Task HashAsync()
    {
        var algo = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("摘要算法：").AddChoices("SM3", "SHA-256"));

        var mode = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("输入类型：").AddChoices("字符串", "文件"));

        if (mode == "字符串")
        {
            var text = AnsiConsole.Ask<string>("输入文本:");
            var result = HashTool.Hash(text, algo);
            if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return; }
            AnsiConsole.WriteLine(result.Data!);
        }
        else
        {
            var path = AnsiConsole.Ask<string>("文件路径:");
            var result = await HashTool.HashFileAsync(path, algo);
            if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return; }
            AnsiConsole.WriteLine(result.Data!);
        }
    }

    private async Task EncryptAsync()
    {
        var text = AnsiConsole.Ask<string>("明文:");
        var masterPath = AnsiConsole.Ask("主密钥文件:", "master.key");
        if (!File.Exists(masterPath)) { IoHelpers.ShowError("FILE_NOT_FOUND", "主密钥文件不存在"); return; }

        var algo = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("算法：").AddChoices("SM4-GCM", "AES-256-GCM"));

        var keyFile = MasterKeyFile.FromJson(await File.ReadAllTextAsync(masterPath));
        var result = EncryptTool.Encrypt(text, keyFile, algo);
        if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return; }

        AnsiConsole.WriteLine(result.Data!);
    }

    private async Task DecryptAsync()
    {
        var cipher = AnsiConsole.Ask<string>("密文:");
        var masterPath = AnsiConsole.Ask("主密钥文件:", "master.key");
        if (!File.Exists(masterPath)) { IoHelpers.ShowError("FILE_NOT_FOUND", "主密钥文件不存在"); return; }

        var algo = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("算法：").AddChoices("SM4-GCM", "AES-256-GCM"));

        var keyFile = MasterKeyFile.FromJson(await File.ReadAllTextAsync(masterPath));
        var result = EncryptTool.Decrypt(cipher, keyFile, algo);
        if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return; }

        AnsiConsole.WriteLine(result.Data!);
    }

    private async Task SignAsync()
    {
        var text = AnsiConsole.Ask<string>("待签名数据:");
        var keyPath = AnsiConsole.Ask<string>("私钥文件:");
        if (!File.Exists(keyPath)) { IoHelpers.ShowError("FILE_NOT_FOUND", "私钥文件不存在"); return; }

        var algo = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("算法：").AddChoices("SM2-SM3", "RSA-SHA256"));

        var pem = await File.ReadAllTextAsync(keyPath);
        var result = SignTool.Sign(text, pem, algo);
        if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return; }

        AnsiConsole.WriteLine(result.Data!);
    }

    private async Task VerifyAsync()
    {
        var text = AnsiConsole.Ask<string>("原始数据:");
        var signature = AnsiConsole.Ask<string>("签名值:");
        var keyPath = AnsiConsole.Ask<string>("公钥文件:");
        if (!File.Exists(keyPath)) { IoHelpers.ShowError("FILE_NOT_FOUND", "公钥文件不存在"); return; }

        var algo = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("算法：").AddChoices("SM2-SM3", "RSA-SHA256"));

        var pem = await File.ReadAllTextAsync(keyPath);
        var result = SignTool.Verify(text, signature, pem, algo);
        if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return; }

        if (result.Data) IoHelpers.ShowSuccess("签名有效");
        else AnsiConsole.MarkupLine("[red]✗ 签名无效[/]");
    }

    private async Task GenLicenseAsync()
    {
        var station = AnsiConsole.Ask("站点编号:", "ST0001");
        var fingerprint = AnsiConsole.Ask<string>("硬件指纹:");
        var expiresStr = AnsiConsole.Ask<string>("到期时间（yyyy-MM-dd）:");
        if (!DateTime.TryParse(expiresStr, out var expires))
        {
            IoHelpers.ShowError("INVALID_DATE", "日期格式错误");
            return;
        }

        var privPath = AnsiConsole.Ask<string>("私钥文件:");
        var masterPath = AnsiConsole.Ask("主密钥文件:", "master.key");
        if (!File.Exists(privPath) || !File.Exists(masterPath))
        {
            IoHelpers.ShowError("FILE_NOT_FOUND", "密钥文件不存在");
            return;
        }

        var priv = await File.ReadAllTextAsync(privPath);
        var keyFile = MasterKeyFile.FromJson(await File.ReadAllTextAsync(masterPath));

        var result = LicenseBuilder.Generate(
            station, fingerprint, expires, "STATION-DESKTOP-1", priv, keyFile);

        if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return; }

        var outPath = AnsiConsole.Ask("输出文件:", "license.lic");
        await File.WriteAllTextAsync(outPath, result.Data!);
        IoHelpers.ShowSuccess($"授权文件已生成：{outPath}");
    }

    private async Task VerifyLicenseAsync()
    {
        var path = AnsiConsole.Ask<string>("授权文件:");
        var pubPath = AnsiConsole.Ask<string>("公钥文件:");
        var masterPath = AnsiConsole.Ask("主密钥文件:", "master.key");

        if (!File.Exists(path) || !File.Exists(pubPath) || !File.Exists(masterPath))
        {
            IoHelpers.ShowError("FILE_NOT_FOUND", "文件不存在");
            return;
        }

        var license = await File.ReadAllTextAsync(path);
        var pub = await File.ReadAllTextAsync(pubPath);
        var keyFile = MasterKeyFile.FromJson(await File.ReadAllTextAsync(masterPath));

        var result = LicenseValidator.Validate(license, pub, keyFile);
        if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return; }

        var check = result.Data!;
        if (check.Valid) IoHelpers.ShowSuccess($"授权有效：{check.Message}");
        else AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(check.Message)}[/]");
    }

    private async Task InspectLicenseAsync()
    {
        var path = AnsiConsole.Ask<string>("授权文件:");
        var masterPath = AnsiConsole.Ask("主密钥文件:", "master.key");

        if (!File.Exists(path) || !File.Exists(masterPath))
        {
            IoHelpers.ShowError("FILE_NOT_FOUND", "文件不存在");
            return;
        }

        var license = await File.ReadAllTextAsync(path);
        var keyFile = MasterKeyFile.FromJson(await File.ReadAllTextAsync(masterPath));

        var result = LicenseInspector.Inspect(license, keyFile);
        if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return; }

        var p = result.Data!;
        var table = new Table().Border(TableBorder.Rounded).AddColumn("属性").AddColumn("值");
        table.AddRow("授权编号", p.LicenseKey);
        table.AddRow("站点编号", p.StationCode);
        table.AddRow("硬件指纹", p.Fingerprint);
        table.AddRow("到期时间", p.ExpiresAt.ToString("yyyy-MM-dd"));
        AnsiConsole.Write(table);
    }

    private async Task GenBindingAsync()
    {
        var serial = AnsiConsole.Ask<string>("记录仪序列号:");
        var model = AnsiConsole.Ask("型号:", "");
        var userNo = AnsiConsole.Ask<string>("用户工号:");
        var userName = AnsiConsole.Ask("用户名:", "");
        var deptCode = AnsiConsole.Ask("部门编码:", "");
        var deptName = AnsiConsole.Ask("部门名:", "");
        var masterPath = AnsiConsole.Ask("主密钥文件:", "master.key");

        if (!File.Exists(masterPath))
        {
            IoHelpers.ShowError("FILE_NOT_FOUND", "主密钥文件不存在");
            return;
        }

        var keyFile = MasterKeyFile.FromJson(await File.ReadAllTextAsync(masterPath));
        var info = new BindingInfo(serial, model, userNo, userName, deptCode, deptName, DateTime.Now, string.Empty);

        var result = BindingFileTool.Generate(info, keyFile);
        if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return; }

        var outPath = AnsiConsole.Ask("输出文件:", "station_bind.ini");
        await File.WriteAllTextAsync(outPath, result.Data!);
        IoHelpers.ShowSuccess($"绑定文件已生成：{outPath}");
    }

    private async Task VerifyBindingAsync()
    {
        var path = AnsiConsole.Ask<string>("绑定文件:");
        var masterPath = AnsiConsole.Ask("主密钥文件:", "master.key");

        if (!File.Exists(path) || !File.Exists(masterPath))
        {
            IoHelpers.ShowError("FILE_NOT_FOUND", "文件不存在");
            return;
        }

        var content = await File.ReadAllTextAsync(path);
        var keyFile = MasterKeyFile.FromJson(await File.ReadAllTextAsync(masterPath));

        var result = BindingFileTool.Validate(content, keyFile);
        if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return; }

        if (result.Data) IoHelpers.ShowSuccess("绑定文件有效");
        else AnsiConsole.MarkupLine("[red]✗ 绑定文件无效或被篡改[/]");
    }

    private Task QueryAuditAsync()
    {
        var dbPath = AnsiConsole.Ask("数据库路径:", "station.db");
        var limit = AnsiConsole.Ask("返回条数:", 20);

        var result = AuditInspector.GetRecent(dbPath, limit);
        if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return Task.CompletedTask; }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("时间")
            .AddColumn("操作人")
            .AddColumn("类型")
            .AddColumn("目标")
            .AddColumn("结果");

        foreach (var row in result.Data!)
        {
            table.AddRow(
                Markup.Escape(row.Time),
                Markup.Escape(row.Operator),
                Markup.Escape(row.OperationType),
                Markup.Escape(row.Target),
                row.Result);
        }
        AnsiConsole.Write(table);
        return Task.CompletedTask;
    }

    private Task QueryPolicyAsync()
    {
        var dbPath = AnsiConsole.Ask("数据库路径:", "station.db");
        var result = PolicyInspector.GetAll(dbPath);
        if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return Task.CompletedTask; }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("用途")
            .AddColumn("算法")
            .AddColumn("组合算法")
            .AddColumn("来源");

        foreach (var row in result.Data!)
        {
            table.AddRow(
                Markup.Escape(row.UsageCode),
                Markup.Escape(row.Algorithm),
                Markup.Escape(row.SecondaryAlgorithm ?? "-"),
                Markup.Escape(row.Source));
        }
        AnsiConsole.Write(table);
        return Task.CompletedTask;
    }

    private Task CheckSchemaAsync()
    {
        var dbPath = AnsiConsole.Ask("数据库路径:", "station.db");
        var result = SchemaInspector.CheckSchema(dbPath);
        if (!result.Success) { IoHelpers.ShowError(result.ErrorCode, result.ErrorMessage); return Task.CompletedTask; }

        var table = new Table().Border(TableBorder.Rounded).AddColumn("表名").AddColumn("状态");
        foreach (var row in result.Data!)
        {
            table.AddRow(
                Markup.Escape(row.TableName),
                row.Exists ? "[green]✓[/]" : "[red]✗[/]");
        }
        AnsiConsole.Write(table);
        return Task.CompletedTask;
    }
}
