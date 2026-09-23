using Spectre.Console;
using Spectre.Console.Cli;
using Station.Tools.Cli.Commands;
using Station.Tools.Cli.Interactive;

namespace Station.Tools.Cli;

/// <summary>采集站工具集入口。</summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        AnsiConsole.Write(new FigletText("Station Tools")
            .LeftJustified()
            .Color(Color.Blue));
        AnsiConsole.MarkupLine("[grey]采集站安全工具集 v1.0[/]\n");

        // 无参数进入交互模式
        if (args.Length == 0)
        {
            var interactive = new InteractiveMenu();
            return await interactive.RunAsync();
        }

        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("station-tools");
            config.ValidateExamples();

            // 密钥
            config.AddBranch("key", key =>
            {
                key.SetDescription("密钥生成与管理");
                key.AddCommand<KeyGenSm2Command>("gen-sm2").WithDescription("生成 SM2 密钥对");
                key.AddCommand<KeyGenRsaCommand>("gen-rsa").WithDescription("生成 RSA 密钥对");
                key.AddCommand<KeyGenMasterCommand>("gen-master").WithDescription("生成主密钥文件");
                key.AddCommand<KeyInspectCommand>("inspect").WithDescription("查看密钥信息");
                key.AddCommand<KeyConvertCommand>("convert").WithDescription("密钥格式转换");
                key.AddCommand<KeyFingerprintCommand>("fingerprint").WithDescription("计算密钥指纹");
            });

            // 加密
            config.AddBranch("crypto", crypto =>
            {
                crypto.SetDescription("加解密与签名");
                crypto.AddCommand<CryptoHashCommand>("hash").WithDescription("计算摘要");
                crypto.AddCommand<CryptoEncryptCommand>("encrypt").WithDescription("对称加密");
                crypto.AddCommand<CryptoDecryptCommand>("decrypt").WithDescription("对称解密");
                crypto.AddCommand<CryptoSignCommand>("sign").WithDescription("签名");
                crypto.AddCommand<CryptoVerifyCommand>("verify").WithDescription("验签");
                crypto.AddCommand<StfeEncryptCommand>("stfe-encrypt").WithDescription("STFE 文件加密");
                crypto.AddCommand<StfeDecryptCommand>("stfe-decrypt").WithDescription("STFE 文件解密");
                crypto.AddCommand<StfeInspectCommand>("stfe-inspect").WithDescription("查看 STFE 文件头");
            });

            // 授权
            config.AddBranch("license", lic =>
            {
                lic.SetDescription("授权文件管理");
                lic.AddCommand<LicenseGenerateCommand>("generate").WithDescription("生成授权文件");
                lic.AddCommand<LicenseVerifyCommand>("verify").WithDescription("验证授权文件");
                lic.AddCommand<LicenseInspectCommand>("inspect").WithDescription("查看授权内容");
                lic.AddCommand<LicenseFingerprintCommand>("fingerprint").WithDescription("查看本机指纹");
            });

            // 绑定
            config.AddBranch("binding", bind =>
            {
                bind.SetDescription("记录仪绑定文件");
                bind.AddCommand<BindingGenerateCommand>("generate").WithDescription("生成绑定文件");
                bind.AddCommand<BindingVerifyCommand>("verify").WithDescription("验证绑定文件");
                bind.AddCommand<BindingInspectCommand>("inspect").WithDescription("查看绑定内容");
            });

            // 检查
            config.AddBranch("inspect", inspect =>
            {
                inspect.SetDescription("系统检查");
                inspect.AddCommand<InspectAuditCommand>("audit").WithDescription("查询审计日志");
                inspect.AddCommand<InspectPolicyCommand>("policy").WithDescription("查询加密策略");
                inspect.AddCommand<InspectCipherCommand>("cipher").WithDescription("判断密文格式");
                inspect.AddCommand<InspectSchemaCommand>("schema").WithDescription("检查表结构");
            });
        });

        try
        {
            return await app.RunAsync(args);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]错误：{ex.Message}[/]");
            return 1;
        }
    }
}
