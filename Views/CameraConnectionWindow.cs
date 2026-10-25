using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using DigitalTwinIndustrialAutomationPlatform.Cameras;

namespace DigitalTwinIndustrialAutomationPlatform.Views;

public sealed class CameraConnectionWindow : Window
{
    private readonly DataGrid _devices = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
        SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, RowHeight = 36 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _refresh = new() { Content = "刷新相机列表", Padding = new Thickness(15, 8, 15, 8) };
    private readonly Button _connect = new() { Content = "选择并连接", Padding = new Thickness(18, 8, 18, 8), IsEnabled = false, IsDefault = true };
    private readonly string _selectedId;
    private CancellationTokenSource? _scan;
    private bool _scanning;
    private bool _isClosing;
    public CameraDeviceInfo? SelectedDevice { get; private set; }

    public CameraConnectionWindow(string selectedId)
    {
        _selectedId = selectedId;
        Title = "连接相机 · 搜索并选择设备"; Width = 1060; Height = 620; MinWidth = 850; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"); FontSize = 13;
        Background = System.Windows.Media.Brushes.White;
        var panel = new DockPanel { Margin = new Thickness(22) }; Content = panel;
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock { Text = "选择相机", FontSize = 22, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "支持普通 USB 摄像头及提供 DirectShow 驱动的工业相机。纯 SDK/GigE 设备需接入厂商采集接口；系统清单仅作提示，不代表可连接。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Foreground = System.Windows.Media.Brushes.SlateGray });
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        footer.Children.Add(_status);
        var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(8, 0, 0, 0) };
        right.Children.Add(_connect); right.Children.Add(cancel); DockPanel.SetDock(right, Dock.Right); buttons.Children.Add(right);
        _refresh.HorizontalAlignment = HorizontalAlignment.Left; buttons.Children.Add(_refresh); footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        _devices.Columns.Add(new DataGridTextColumn { Header = "设备名称", Binding = new Binding("Name"), Width = new DataGridLength(2, DataGridLengthUnitType.Star), MinWidth = 170 });
        _devices.Columns.Add(new DataGridTextColumn { Header = "编号", Binding = new Binding("IndexText"), Width = 50 });
        _devices.Columns.Add(new DataGridTextColumn { Header = "采集接口", Binding = new Binding("Provider"), Width = 170 });
        _devices.Columns.Add(new DataGridTextColumn { Header = "状态", Binding = new Binding("Status"), Width = 260 });
        _devices.Columns.Add(new DataGridTextColumn { Header = "设备路径", Binding = new Binding("DevicePath"), Width = 260 });
        panel.Children.Add(_devices);
        _devices.SelectionChanged += (_, _) => _connect.IsEnabled = !_scanning && (_devices.SelectedItem as CameraDeviceInfo)?.CanCapture == true;
        _devices.MouseDoubleClick += (_, e) => { if (_connect.IsEnabled && e.OriginalSource is DependencyObject origin && ItemsControl.ContainerFromElement(_devices, origin) is DataGridRow) SelectDevice(); };
        _connect.Click += (_, _) => SelectDevice(); _refresh.Click += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => await RefreshAsync();
        Closing += (_, _) => { _isClosing = true; _scan?.Cancel(); };
    }
    private void SelectDevice()
    {
        if (_scanning || _devices.SelectedItem is not CameraDeviceInfo { CanCapture: true } device) return;
        SelectedDevice = device; DialogResult = true;
    }
    private async Task RefreshAsync()
    {
        if (_scanning || _isClosing) return;
        var previous = (_devices.SelectedItem as CameraDeviceInfo)?.Id ?? _selectedId;
        _devices.ItemsSource = null;
        _scanning = true; _refresh.IsEnabled = _connect.IsEnabled = false; _devices.IsEnabled = false;
        var session = new CancellationTokenSource(); _scan = session;
        _status.Text = "正在扫描视频采集设备和系统相机清单……（扫描不会逐个打开相机）";
        try
        {
            var result = await CameraDiscovery.ScanAsync(session.Token);
            if (_isClosing) return;
            _devices.ItemsSource = result.Devices;
            _devices.SelectedItem = result.Devices.FirstOrDefault(d => d.Id.Equals(previous, StringComparison.OrdinalIgnoreCase))
                ?? result.Devices.FirstOrDefault(d => d.CanCapture) ?? result.Devices.FirstOrDefault();
            var usable = result.Devices.Count(d => d.CanCapture);
            _status.Text = $"找到 {usable} 个可尝试连接的视频设备，{result.Devices.Count - usable} 个系统/SDK 候选设备。" +
                (result.Devices.Count == 0 ? " 请检查连接、驱动及相机隐私权限，然后刷新。" : " 选择后点击连接，在主界面验证实际图像。") +
                (result.Warnings.Count > 0 ? "\n" + string.Join("\n", result.Warnings) : string.Empty);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_isClosing) _status.Text = "搜索失败：" + ex.GetBaseException().Message; }
        finally
        {
            _scan = null; session.Dispose(); _scanning = false;
            if (!_isClosing) { _refresh.IsEnabled = _devices.IsEnabled = true; _connect.IsEnabled = (_devices.SelectedItem as CameraDeviceInfo)?.CanCapture == true; }
        }
    }
}
