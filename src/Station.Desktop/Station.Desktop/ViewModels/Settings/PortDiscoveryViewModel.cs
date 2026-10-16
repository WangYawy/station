using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Station.Application.UsbPortCards;

namespace Station.Desktop.ViewModels.Settings;

/// <summary>
/// 端口发现页面 VM。
/// 
/// 【职责】
///   - 每 1 秒轮询 IUsbPortCardService.GetDiscoveryListAsync 刷新列表；
///   - 支持手动指定序号（1-30）；
///   - 支持一键复制映射表 JSON；
///   - 支持删除映射；
///   - 支持保存并热加载。
/// 
/// 【不影响采集】只读观察，不触发识别 / 采集流程。
/// </summary>
public partial class PortDiscoveryViewModel : ObservableObject, IDisposable
{
    private readonly IUsbPortCardService _cardService;
    private readonly IUsbPortMapService _portMap;
    private readonly ILogger<PortDiscoveryViewModel> _logger;
    private readonly DispatcherTimer _refreshTimer;

    private int _refreshing;

    public ObservableCollection<PortDiscoveryItemViewModel> Items { get; } = new();

    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private PortDiscoveryItemViewModel? _selectedItem;

    public IRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand CopyJsonCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }

    public PortDiscoveryViewModel(
        IUsbPortCardService cardService,
        IUsbPortMapService portMap,
        ILogger<PortDiscoveryViewModel> logger)
    {
        _cardService = cardService;
        _portMap = portMap;
        _logger = logger;

        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
        CopyJsonCommand = new AsyncRelayCommand(CopyJsonAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (_, _) => _ = RefreshAsync();
        _refreshTimer.Start();

        _ = RefreshAsync();
    }

    // =========================================================
    // 刷新
    // =========================================================

    private async Task RefreshAsync()
    {
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;
        try
        {
            var list = await _cardService.GetDiscoveryListAsync();

            // 保留用户当前选中项（按 PhysicalPortId 匹配）
            var selectedPortId = SelectedItem?.PhysicalPortId;

            // 用现有 Item 复用，避免 UI 抖动（若 Item 有输入框编辑中）
            var existing = Items.ToDictionary(i => i.PhysicalPortId ?? string.Empty,
                                              i => i, StringComparer.OrdinalIgnoreCase);
            Items.Clear();

            foreach (var dto in list)
            {
                if (dto.PhysicalPortId is { } id && existing.TryGetValue(id, out var keep))
                {
                    keep.Update(dto);
                    Items.Add(keep);
                }
                else
                {
                    var vm = new PortDiscoveryItemViewModel(dto, AssignPortNoAsync, RemoveMappingAsync);
                    Items.Add(vm);
                }
            }

            // 恢复选中
            if (!string.IsNullOrEmpty(selectedPortId))
                SelectedItem = Items.FirstOrDefault(i =>
                    string.Equals(i.PhysicalPortId, selectedPortId, StringComparison.OrdinalIgnoreCase));

            StatusText = $"共 {Items.Count} 条（{Items.Count(i => i.IsConnected)} 已连接）";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "端口发现刷新失败");
            StatusText = $"刷新失败：{ex.Message}";
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    // =========================================================
    // 命令
    // =========================================================

    private async Task CopyJsonAsync()
    {
        try
        {
            var json = _portMap.ExportJson();
            var top = Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (top?.Clipboard is { } cb)
            {
                await cb.SetTextAsync(json);
                StatusText = "映射表 JSON 已复制到剪贴板";
            }
            else
            {
                StatusText = "剪贴板不可用";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "复制 JSON 失败");
            StatusText = $"复制失败：{ex.Message}";
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            IsBusy = true;
            await _portMap.SaveAsync();
            StatusText = "映射表已保存并热加载";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存映射表失败");
            StatusText = $"保存失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>手动指定某行序号（1-30）。</summary>
    private async Task AssignPortNoAsync(PortDiscoveryItemViewModel item, int portNo)
    {
        if (item.PhysicalPortId is not { } pid || string.IsNullOrEmpty(pid))
        {
            StatusText = "该行没有物理端口标识，无法映射";
            return;
        }
        if (portNo < 1 || portNo > 30)
        {
            StatusText = "序号必须为 1-30";
            return;
        }

        try
        {
            await _portMap.SetMappingAsync(pid, portNo);
            item.ApplyMapping(portNo);
            StatusText = $"已指定 {pid} → #{portNo:D2}（未保存，点击保存生效）";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "指定端口失败");
            StatusText = $"指定失败：{ex.Message}";
        }
    }

    /// <summary>删除映射。</summary>
    private async Task RemoveMappingAsync(PortDiscoveryItemViewModel item)
    {
        if (item.PhysicalPortId is not { } pid || string.IsNullOrEmpty(pid)) return;

        try
        {
            await _portMap.RemoveMappingAsync(pid);
            item.ApplyMapping(null);
            StatusText = $"已删除 {pid} 的映射（未保存）";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "删除映射失败");
            StatusText = $"删除失败：{ex.Message}";
        }
    }

    public void Dispose() => _refreshTimer.Stop();
}

/// <summary>端口发现单行 VM。</summary>
public partial class PortDiscoveryItemViewModel : ObservableObject
{
    private readonly Func<PortDiscoveryItemViewModel, int, Task> _assign;
    private readonly Func<PortDiscoveryItemViewModel, Task> _remove;

    public PortDiscoveryItemViewModel(
        UsbPortDiscoveryItemDto dto,
        Func<PortDiscoveryItemViewModel, int, Task> assign,
        Func<PortDiscoveryItemViewModel, Task> remove)
    {
        _assign = assign;
        _remove = remove;

        Update(dto);

        AssignCommand = new AsyncRelayCommand(AssignCoreAsync);
        RemoveCommand = new AsyncRelayCommand(() => _remove(this));
    }

    [ObservableProperty] private string? _physicalPortId;
    [ObservableProperty] private string _deviceName = string.Empty;
    [ObservableProperty] private string? _protocolText;
    [ObservableProperty] private int? _displayPortNo;
    [ObservableProperty] private bool _isMapped;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _displayPortText = "#??";

    /// <summary>用户输入框内容（1-30）。</summary>
    [ObservableProperty] private string _inputPortNo = string.Empty;

    public IAsyncRelayCommand AssignCommand { get; }
    public IAsyncRelayCommand RemoveCommand { get; }

    /// <summary>由父 VM 刷新时调用，只更新只读字段，不覆盖用户输入。</summary>
    public void Update(UsbPortDiscoveryItemDto dto)
    {
        PhysicalPortId = dto.PhysicalPortId;
        DeviceName = dto.DeviceName;
        ProtocolText = dto.Protocol?.ToString();
        IsConnected = dto.IsConnected;

        DisplayPortNo = dto.DisplayPortNo;
        IsMapped = dto.IsMapped;
        DisplayPortText = dto.DisplayPortNo is { } n ? $"#{n:D2}" : "#??";
    }

    /// <summary>由父 VM 在映射变更后调用（立即反映到 UI）。</summary>
    public void ApplyMapping(int? portNo)
    {
        DisplayPortNo = portNo;
        IsMapped = portNo.HasValue;
        DisplayPortText = portNo is { } n ? $"#{n:D2}" : "#??";
        InputPortNo = string.Empty;
    }

    private async Task AssignCoreAsync()
    {
        if (!int.TryParse(InputPortNo?.Trim(), out var n))
            return;
        await _assign(this, n);
    }
}
