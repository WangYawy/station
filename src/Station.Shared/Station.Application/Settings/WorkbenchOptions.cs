namespace Station.Application.Settings;

/// <summary>
/// 工作台 30 路（可配置）USB 采集卡片布局，对应配置节 <c>Station:Workbench</c>：
/// 行数 × 每行卡片数 = 卡片总数；卡片宽高用于工作台动态排布。
/// </summary>
public sealed class WorkbenchOptions
{
    public const string SectionName = "Station:Workbench";

    public int Rows { get; set; } = 6;

    public int Columns { get; set; } = 5;

    public int CardWidth { get; set; } = 240;

    public int CardHeight { get; set; } = 200;
}
