using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DigitalTwinIndustrialAutomationPlatform.Models;
using DigitalTwinIndustrialAutomationPlatform.Services;

namespace DigitalTwinIndustrialAutomationPlatform.Views;

public enum ProcessingSettingsSection { Filter, Edges, Contours, Morphology, Match }

/// <summary>按类别构建表单，始终在打开时的副本上修改，只写回当前类别中启用的字段。</summary>
public sealed class SettingsEditor : UserControl
{
    private readonly ProcessingSettingsSection _section;
    private ProcessingSettings _snapshot = new();
    private readonly Dictionary<Control, FrameworkElement> _rows = new();
    private readonly List<(Control Control, Action<ProcessingSettings> Write)> _writers = new();
    private readonly ComboBox _filter = new() { ItemsSource = ImageAlgorithms.FilterNames };
    private readonly ComboBox _edge = new() { ItemsSource = ImageAlgorithms.EdgeNames };
    private readonly ComboBox _segmentation = new() { ItemsSource = new[] { "固定阈值", "灰度区间（含上下限）", "Otsu 自动阈值" } };
    private readonly ComboBox _morph = new() { ItemsSource = new[] { "开运算", "闭运算", "腐蚀", "膨胀" } };
    private readonly TextBox _threshold = new(), _upper = new(), _kernel = new(), _derivativeKernel = new();
    private readonly TextBox _sigma = new(), _sigmaColor = new(), _sigmaSpace = new();
    private readonly TextBox _low = new(), _high = new(), _edgeThreshold = new(), _closingSize = new(), _area = new(), _score = new();
    private readonly CheckBox _invert = new() { Content = "反选前景（固定阈值时为暗色前景）" };
    private readonly CheckBox _closeGaps = new() { Content = "使用闭运算连接边缘断点" };

    public SettingsEditor(ProcessingSettingsSection section)
    {
        _section = section;
        var panel = new StackPanel(); Content = panel;
        void Row(string label, Control control, Action<ProcessingSettings> write)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 14, 0) });
            control.MinHeight = 34; control.VerticalContentAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1); row.Children.Add(control); panel.Children.Add(row);
            _rows.Add(control, row); _writers.Add((control, write));
        }
        if (section == ProcessingSettingsSection.Filter)
        {
            Row("滤波方式", _filter, s => s.Filter = (FilterKind)_filter.SelectedIndex);
            Row("平滑 / LoG 核（3～31，奇数）", _kernel, s => s.KernelSize = int.Parse(_kernel.Text));
            Row("微分核（3、5、7）", _derivativeKernel, s => s.DerivativeKernelSize = int.Parse(_derivativeKernel.Text));
            Row("高斯 Sigma（0 自动，≤100）", _sigma, s => s.GaussianSigma = double.Parse(_sigma.Text));
            Row("双边灰度 Sigma（>0，≤1000）", _sigmaColor, s => s.BilateralSigmaColor = double.Parse(_sigmaColor.Text));
            Row("双边空间 Sigma（>0，≤1000）", _sigmaSpace, s => s.BilateralSigmaSpace = double.Parse(_sigmaSpace.Text));
        }
        if (section is ProcessingSettingsSection.Edges or ProcessingSettingsSection.Contours)
        {
            Row("轮廓来源 / 边缘算子", _edge, s => s.Edge = (EdgeKind)_edge.SelectedIndex);
            Row("微分 / Canny 核（3、5、7）", _derivativeKernel, s => s.DerivativeKernelSize = int.Parse(_derivativeKernel.Text));
            Row("梯度幅值阈值（0～255）", _edgeThreshold, s => s.EdgeThreshold = int.Parse(_edgeThreshold.Text));
            Row("Canny 低阈值（0～254）", _low, s => s.CannyLow = int.Parse(_low.Text));
            Row("Canny 高阈值（1～255）", _high, s => s.CannyHigh = int.Parse(_high.Text));
            Row("LoG 平滑核（3～31，奇数）", _kernel, s => s.KernelSize = int.Parse(_kernel.Text));
            Row("LoG 高斯 Sigma（0 自动）", _sigma, s => s.GaussianSigma = double.Parse(_sigma.Text));
            Row("边缘连接", _closeGaps, s => s.CloseEdgeGaps = _closeGaps.IsChecked == true);
            Row("闭运算核（3～15，奇数）", _closingSize, s => s.EdgeClosingSize = int.Parse(_closingSize.Text));
        }
        if (section is ProcessingSettingsSection.Contours or ProcessingSettingsSection.Morphology)
        {
            Row("灰度分割方式", _segmentation, s => s.Segmentation = (SegmentationKind)_segmentation.SelectedIndex);
            Row("固定阈值 / 区间下限（0～255）", _threshold, s => s.Threshold = int.Parse(_threshold.Text));
            Row("区间上限（0～255）", _upper, s => s.ThresholdUpper = int.Parse(_upper.Text));
            Row("前景方向", _invert, s => s.Invert = _invert.IsChecked == true);
        }
        if (section == ProcessingSettingsSection.Contours)
            Row("最小轮廓面积（px²）", _area, s => s.MinArea = double.Parse(_area.Text));
        if (section == ProcessingSettingsSection.Morphology)
        {
            Row("形态学操作", _morph, s => s.Morphology = (MorphKind)_morph.SelectedIndex);
            Row("形态学核（3～31，奇数）", _kernel, s => s.KernelSize = int.Parse(_kernel.Text));
        }
        if (section == ProcessingSettingsSection.Match)
            Row("模板通过分数（0～1）", _score, s => s.MatchScore = double.Parse(_score.Text));
        _filter.SelectionChanged += (_, _) => UpdateParameterAvailability();
        _edge.SelectionChanged += (_, _) => UpdateParameterAvailability();
        _segmentation.SelectionChanged += (_, _) => UpdateParameterAvailability();
        _closeGaps.Checked += (_, _) => UpdateParameterAvailability();
        _closeGaps.Unchecked += (_, _) => UpdateParameterAvailability();
    }

    public void Load(ProcessingSettings s)
    {
        _snapshot = s.Copy();
        _threshold.Text = s.Threshold.ToString(); _upper.Text = s.ThresholdUpper.ToString();
        _kernel.Text = s.KernelSize.ToString(); _derivativeKernel.Text = s.DerivativeKernelSize.ToString();
        _low.Text = s.CannyLow.ToString(); _high.Text = s.CannyHigh.ToString();
        _edgeThreshold.Text = s.EdgeThreshold.ToString(); _closingSize.Text = s.EdgeClosingSize.ToString();
        _sigma.Text = s.GaussianSigma.ToString(CultureInfo.CurrentCulture);
        _sigmaColor.Text = s.BilateralSigmaColor.ToString(CultureInfo.CurrentCulture);
        _sigmaSpace.Text = s.BilateralSigmaSpace.ToString(CultureInfo.CurrentCulture);
        _area.Text = s.MinArea.ToString(CultureInfo.CurrentCulture); _score.Text = s.MatchScore.ToString(CultureInfo.CurrentCulture);
        _invert.IsChecked = s.Invert; _closeGaps.IsChecked = s.CloseEdgeGaps;
        _filter.SelectedIndex = (int)s.Filter; _edge.SelectedIndex = (int)s.Edge;
        _segmentation.SelectedIndex = (int)s.Segmentation; _morph.SelectedIndex = (int)s.Morphology;
        UpdateParameterAvailability();
    }

    private void Active(Control control, bool active)
    {
        if (!_rows.TryGetValue(control, out var row)) return;
        row.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        control.IsEnabled = active;
    }
    private void UpdateParameterAvailability()
    {
        var f = (FilterKind)_filter.SelectedIndex; var e = (EdgeKind)_edge.SelectedIndex;
        var segmentation = (SegmentationKind)_segmentation.SelectedIndex;
        var filter = _section == ProcessingSettingsSection.Filter;
        var gray = _section == ProcessingSettingsSection.Morphology || e == EdgeKind.ThresholdContour;
        Active(_segmentation, gray); Active(_threshold, gray && segmentation != SegmentationKind.Otsu);
        Active(_upper, gray && segmentation == SegmentationKind.Range); Active(_invert, gray);
        Active(_low, e == EdgeKind.Canny); Active(_high, e == EdgeKind.Canny);
        Active(_edgeThreshold, e >= EdgeKind.Sobel);
        Active(_closeGaps, !gray); Active(_closingSize, !gray && _closeGaps.IsChecked == true);
        Active(_sigma, filter ? f is FilterKind.Gaussian or FilterKind.LoG : e == EdgeKind.LoG);
        Active(_sigmaColor, f == FilterKind.Bilateral); Active(_sigmaSpace, f == FilterKind.Bilateral);
        Active(_derivativeKernel, filter ? f is FilterKind.Sobel or FilterKind.Laplacian or FilterKind.LoG : e is EdgeKind.Canny or EdgeKind.Sobel or EdgeKind.Laplacian or EdgeKind.LoG);
        Active(_kernel, _section == ProcessingSettingsSection.Morphology || (filter ? f is FilterKind.Gaussian or FilterKind.Median or FilterKind.Bilateral or FilterKind.Mean or FilterKind.LoG : e == EdgeKind.LoG));
    }

    public ProcessingSettings ReadSettings()
    {
        var result = _snapshot.Copy();
        foreach (var (control, write) in _writers)
            if (control.IsEnabled) write(result);
        result.Validate();
        return result;
    }
}
