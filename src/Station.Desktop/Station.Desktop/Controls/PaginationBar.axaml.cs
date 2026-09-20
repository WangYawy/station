// 放置位置：Station.Desktop/Controls/PaginationBar.axaml.cs

using Avalonia.Controls;

namespace Station.Desktop.Controls;

/// <summary>
/// 通用分页条。本身不含任何业务逻辑，全部状态与命令来自 <see cref="ViewModels.PaginationViewModel"/>。<br/>
/// 用法（DataContext 必须显式指向页面 VM 上的 Pager 属性）：<br/>
/// <c>&lt;controls:PaginationBar DataContext="{Binding Pager}" /&gt;</c>
/// </summary>
public partial class PaginationBar : UserControl
{
    public PaginationBar()
    {
        InitializeComponent();
    }
}
