using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DigitalTwinIndustrialAutomationPlatform.Models;

namespace DigitalTwinIndustrialAutomationPlatform.Views;

/// <summary>模态编辑副本；验证及磁盘保存成功才返回结果。取消/关闭不写入。</summary>
public sealed class ProcessingSettingsWindow : Window
{
    private readonly SettingsEditor _editor;
    private readonly ProcessingSettings _original;
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    public ProcessingSettings? ResultSettings { get; private set; }
    public static string SectionTitle(ProcessingSettingsSection section) => section switch
    {
        ProcessingSettingsSection.Filter => "滤波参数设置",
        ProcessingSettingsSection.Edges => "边缘检测参数设置",
        ProcessingSettingsSection.Contours => "轮廓提取参数设置",
        ProcessingSettingsSection.Morphology => "形态学参数设置",
        _ => "模板匹配参数设置"
    };
    public ProcessingSettingsWindow(ProcessingSettings settings, ProcessingSettingsSection section)
    {
        _original = settings.Copy();
        Title = SectionTitle(section); Width = 690; Height = section == ProcessingSettingsSection.Match ? 380 : 640;
        MinWidth = 620; MinHeight = 340; MaxHeight = Math.Max(340, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
        Background = new SolidColorBrush(Color.FromRgb(243, 245, 249));
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "保存后生效；取消或关闭窗口会丢弃本次修改。所有处理先转灰度。", Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        footer.Children.Add(_error);
        var buttons = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var save = new Button { Content = "保存设置", IsDefault = true, Padding = new Thickness(18, 9, 18, 9), Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)), Foreground = Brushes.White };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(8, 0, 0, 0) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(save); right.Children.Add(cancel); DockPanel.SetDock(right, Dock.Right); buttons.Children.Add(right);
        var revert = new Button { Content = "还原本次修改", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 9, 12, 9) };
        buttons.Children.Add(revert); footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        _editor = new SettingsEditor(section); _editor.Load(_original);
        var body = new StackPanel(); body.Children.Add(_editor);
        body.Children.Add(new TextBlock { Text = section switch
        {
            ProcessingSettingsSection.Filter => "切换滤波类型仅显示适用参数。Scharr/Prewitt 核固定 3×3，Roberts 固定 2×2。",
            ProcessingSettingsSection.Edges => "选择灰度阈值轮廓时，请在“轮廓设置”调整分割阈值。",
            ProcessingSettingsSection.Contours => "选择灰度分割或边缘算子后，显示对应阈值及参数；面积与闭合周长按外轮廓测量。",
            ProcessingSettingsSection.Morphology => "此处作用于手动形态学工具；产品边缘连接的闭运算请在边缘或轮廓设置中配置。",
            _ => "模板匹配为固定尺寸、固定角度的单目标匹配。"
        } + "\n核大小、微分核和高斯 Sigma 为共用参数。修改后请重新测量建档；自动分拣沿用产品类保存的配方。", Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(20), Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = body } });
        revert.Click += (_, _) => { _editor.Load(_original); _error.Text = string.Empty; };
        save.Click += (_, _) =>
        {
            try
            {
                var candidate = _editor.ReadSettings();
                candidate.Save(); // 保存失败保留弹窗与输入，不能覆盖正在使用的参数。
                ResultSettings = candidate; DialogResult = true;
            }
            catch (Exception ex) { _error.Text = ex is FormatException or OverflowException ? "参数格式无效，请输入有效范围内的数字。" : ex.Message; }
        };
    }
}
