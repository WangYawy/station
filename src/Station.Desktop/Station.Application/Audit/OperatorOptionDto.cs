
namespace Station.Domain.Audit;
/// <summary>操作人下拉项。<see cref="UserNo"/> 作为筛选值，UserName 作显示。</summary>
public sealed record OperatorOptionDto(string UserNo, string UserName);
