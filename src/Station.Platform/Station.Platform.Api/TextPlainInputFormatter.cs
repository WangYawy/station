using System.Text;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace Station.Platform.Api;

/// <summary>纯文本输入格式器：支持 text/plain / text/csv 绑定到 string（用于 CSV 导入）。</summary>
public sealed class TextPlainInputFormatter : TextInputFormatter
{
    public TextPlainInputFormatter()
    {
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("text/plain"));
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("text/csv"));
        SupportedEncodings.Add(Encoding.UTF8);
        SupportedEncodings.Add(Encoding.Unicode);
    }

    protected override bool CanReadType(Type type) => type == typeof(string);

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(
        InputFormatterContext context,
        Encoding encoding)
    {
        using var reader = new StreamReader(context.HttpContext.Request.Body, encoding);
        var content = await reader.ReadToEndAsync();
        return await InputFormatterResult.SuccessAsync(content);
    }
}
