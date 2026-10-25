using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using DigitalTwinIndustrialAutomationPlatform.Models;
using DigitalTwinIndustrialAutomationPlatform.Cameras;
using DigitalTwinIndustrialAutomationPlatform.Recognition;
using DigitalTwinIndustrialAutomationPlatform.Services;
using DigitalTwinIndustrialAutomationPlatform.Views;

namespace DigitalTwinIndustrialAutomationPlatform;

public partial class MainWindow : System.Windows.Window
{
    private const string ImageFilter = "图像文件|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|所有文件|*.*";
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };
    private readonly ObservableCollection<ImageFileItem> _files = new();
    private readonly List<DetectionRecord> _records = new();
    private readonly ICollectionView _fileView;
    private readonly DispatcherTimer _thresholdTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private ProcessingSettings _settings = ProcessingSettings.Load();
    private Mat? _source;
    private Mat? _template;
    private ProcessingResult? _processed;
    private string _sourceName = string.Empty;
    private string _templatePath = string.Empty;
    private string _search = string.Empty;
    private ImageOperation _operation = ImageOperation.Original;
    private CancellationTokenSource? _cameraCts;
    private Task? _cameraTask;
    private bool _cameraHasFrame;
    private bool _busy;
    private Task? _busyAction;
    private readonly ShutdownCoordinator _exitCoordinator = new();
    private bool _workersStopped = true;
    private bool _initialized;
    private volatile bool _closed;

    public MainWindow()
    {
        InitializeComponent();
        _fileView = CollectionViewSource.GetDefaultView(_files);
        _fileView.Filter = item => item is ImageFileItem file &&
            (string.IsNullOrEmpty(_search) || file.FullPath.Contains(_search, StringComparison.OrdinalIgnoreCase));
        FilesGrid.ItemsSource = _fileView;
        ThresholdSlider.Value = _settings.Threshold;
        ThresholdBox.Text = _settings.Threshold.ToString();
        InvertCheck.IsChecked = _settings.Invert;
        _thresholdTimer.Tick += ThresholdTimer_Tick;
        _initialized = true;
        InitializeCommands();
        InitializeNavigation();
        InitializeRecognition();
        UpdateControls();
    }

    private bool Ready(bool needsImage = true, bool allowCamera = false)
    {
        if (_closed || _closing || _busy || _navigating || _automaticTask is not null || !IsRecognitionModule) return false;
        if (!allowCamera && _cameraTask is not null)
        {
            StatusText.Text = "请先点击“采样”或“停止”，再处理当前帧。";
            return false;
        }
        if (needsImage && _source is null)
        {
            StatusText.Text = "请先打开图片，或开始摄像头采集。";
            return false;
        }
        return true;
    }

    private async Task BusyAsync(string status, Func<Task> action, bool finishBeforeExit = false)
    {
        if (_busy || _closed || _closing) return;
        _busy = true; _thresholdTimer.Stop(); UpdateControls(); StatusText.Text = status;
        try { _busyAction = finishBeforeExit ? _exitCoordinator.RunWriteAsync(action) : action(); await _busyAction; }
        catch (Exception ex) { if (!_closed && !_closing) ShowError(ex); }
        finally { _busyAction = null; _busy = false; if (!_closed) UpdateControls(); }
    }

    private void UpdateControls()
    {
        ActionToolbar.IsEnabled = !_busy && !_navigating && !_closing;
        NavigationPanel.IsEnabled = HomePage.IsEnabled = SettingsPage.IsEnabled = !_busy && !_navigating && !_closing;
        FilesGrid.IsEnabled = !_busy && !_closing && _cameraTask is null && _automaticTask is null;
        ThresholdSlider.IsEnabled = ThresholdBox.IsEnabled = InvertCheck.IsEnabled = !_busy && !_closing && _automaticTask is null;
        StartButton.IsEnabled = !_busy && !_closing && _cameraTask is null && _automaticTask is null;
        StopButton.IsEnabled = !_busy && !_closing && _cameraTask is not null && _automaticTask is null;
        BusyIndicator.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
        ActivityText.Text = _automaticTask is not null ? "自动识别中" : _navigating ? "正在切换" : _busy ? "处理中" : _cameraTask is not null ? "摄像头预览"
            : IsRecognitionModule ? (_source is null ? "待命" : "图像就绪")
            : _page == AppPage.Home ? "平台首页" : _page == AppPage.Settings ? "平台设置" : _page == AppPage.About ? "关于平台" : "模块就绪";
        FilesSummary.Text = string.IsNullOrEmpty(_search) ? $"{_files.Count} 个文件" : $"{_fileView.Cast<object>().Count()} / {_files.Count} 个文件";
        UpdateRecognitionControls();
        UpdateCameraStatus();
        CommandManager.InvalidateRequerySuggested();
    }

    private void ShowError(Exception error)
    {
        var cause = error.GetBaseException();
        var text = cause.Message;
        if (cause is DllNotFoundException or BadImageFormatException)
            text = "OpenCV 原生库加载失败。请还原 NuGet 包，以 Windows x64 运行，并检查 OpenCvSharpExtern.dll 及其依赖。\n" + text;
        StatusText.Text = "操作失败 · " + text;
        MessageBox.Show(this, text, "操作提示", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready(false)) return;
        var dialog = new OpenFileDialog { Title = "打开产品图片（可多选）", Filter = ImageFilter, Multiselect = true };
        if (dialog.ShowDialog(this) == true) await ImportFilesAsync(dialog.FileNames);
    }

    private async void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready(false)) return;
        var dialog = new OpenFolderDialog { Title = "选择图片文件夹（导入本层图片）" };
        if (dialog.ShowDialog(this) != true) return;
        await BusyAsync("正在读取文件夹……", async () =>
        {
            var paths = await Task.Run(() => Directory.EnumerateFiles(dialog.FolderName)
                .Where(path => ImageExtensions.Contains(Path.GetExtension(path))).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray());
            await ImportCoreAsync(paths);
        });
    }

    private Task ImportFilesAsync(string[] paths) => BusyAsync("正在读取文件……", () => ImportCoreAsync(paths));

    private async Task ImportCoreAsync(string[] paths)
    {
        var items = await Task.Run(() => paths.Select(ImageFileItem.FromPath).ToArray());
        if (_closed || _closing) return;
        if (items.Length == 0) { StatusText.Text = "未找到受支持的图片。"; return; }
        var existing = new HashSet<string>(_files.Select(file => file.FullPath), StringComparer.OrdinalIgnoreCase);
        foreach (var item in items) if (existing.Add(item.FullPath)) _files.Add(item);
        SearchBox.Text = string.Empty; _search = string.Empty; _fileView.Refresh();
        FilesGrid.SelectedItem = _files.First(file => string.Equals(file.FullPath, items[0].FullPath, StringComparison.OrdinalIgnoreCase));
        await LoadCoreAsync(items[0].FullPath);
    }

    private async Task LoadCoreAsync(string path)
    {
        using var loaded = await Task.Run(() => OpenCvImageService.LoadColor(path));
        if (_closed || _closing) return;
        SetSource(loaded, path);
        StatusText.Text = $"已加载 · {Path.GetFileName(path)} · {loaded.Width} × {loaded.Height} · 共 {_files.Count} 个文件";
    }

    private void SetSource(Mat image, string name)
    {
        var bitmap = image.ToBitmapSource(); bitmap.Freeze();
        var next = image.Clone();
        InvalidateTrainingSample();
        _source?.Dispose(); _source = next;
        _processed?.Dispose(); _processed = null;
        _operation = ImageOperation.Original; _sourceName = name;
        PreviewImage.Source = bitmap; EmptyHint.Visibility = Visibility.Collapsed; PathText.Text = name;
        UpdateControls();
    }

    private async void FilesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || !Ready(false) || FilesGrid.SelectedItem is not ImageFileItem item) return;
        await BusyAsync("正在打开图片……", () => LoadCoreAsync(item.FullPath));
    }

    private void FilesGrid_LoadingRow(object sender, DataGridRowEventArgs e) => e.Row.Header = (e.Row.GetIndex() + 1).ToString();

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_initialized) return;
        _search = SearchBox.Text.Trim(); _fileView.Refresh();
        StatusText.Text = $"搜索结果：{_fileView.Cast<object>().Count()} / {_files.Count} 个文件";
        UpdateControls();
    }
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { e.Handled = true; Search_Click(sender, e); } }

    private async Task ProcessAsync(ImageOperation operation, bool recordDetection = true)
    {
        if (!Ready()) return;
        CommitThreshold();
        await BusyAsync("正在处理图像……", async () =>
        {
            using var snapshot = _source!.Clone();
            using var template = _template?.Clone();
            var settings = _settings.Copy(); var sourceName = _sourceName;
            var watch = Stopwatch.StartNew();
            ProcessingResult? result = await Task.Run(() => ImageProcessor.Apply(snapshot, operation, settings, template));
            try
            {
                if (_closed || _closing) return;
                var bitmap = result.Image.ToBitmapSource(); bitmap.Freeze();
                InvalidateTrainingSample();
                _processed?.Dispose(); _processed = result; result = null;
                _operation = operation; PreviewImage.Source = bitmap;
                watch.Stop(); StatusText.Text = $"{_processed.Description} · {watch.Elapsed.TotalMilliseconds:F0} ms";
                if (recordDetection && operation == ImageOperation.Detect)
                    _records.Add(new(DateTime.Now, sourceName, settings.Threshold, settings.Invert,
                        _processed.Count, _processed.TotalArea, watch.Elapsed.TotalMilliseconds));
            }
            finally { result?.Dispose(); }
        });
    }

    private async void Grayscale_Click(object sender, RoutedEventArgs e) => await ProcessAsync(ImageOperation.Grayscale);
    private async void Filter_Click(object sender, RoutedEventArgs e) => await ProcessAsync(ImageOperation.Filter);
    private async void Edges_Click(object sender, RoutedEventArgs e) => await ProcessAsync(ImageOperation.Edges);
    private async void Contours_Click(object sender, RoutedEventArgs e) => await ProcessAsync(ImageOperation.Contours);
    private async void Detect_Click(object sender, RoutedEventArgs e) => await ProcessAsync(ImageOperation.Detect);
    private async void Morphology_Click(object sender, RoutedEventArgs e) => await ProcessAsync(ImageOperation.Morphology);
    private async void Reset_Click(object sender, RoutedEventArgs e) => await ProcessAsync(ImageOperation.Original);

    private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        if (_settings.Segmentation == SegmentationKind.Range && ThresholdSlider.Value > _settings.ThresholdUpper)
        { ThresholdSlider.Value = _settings.ThresholdUpper; return; }
        _settings.Threshold = (int)Math.Round(ThresholdSlider.Value);
        if (_trainingRecipe is not null && Recognition.ProductPipeline.RecipeKey(_trainingRecipe) != Recognition.ProductPipeline.RecipeKey(_settings)) InvalidateTrainingSample();
        ThresholdBox.Text = _settings.Threshold.ToString(); ScheduleThresholdRefresh();
    }
    private void ThresholdUp_Click(object sender, RoutedEventArgs e) => ThresholdSlider.Value = Math.Min(255, Math.Round(ThresholdSlider.Value) + 1);
    private void ThresholdDown_Click(object sender, RoutedEventArgs e) => ThresholdSlider.Value = Math.Max(0, Math.Round(ThresholdSlider.Value) - 1);
    private void ThresholdBox_LostFocus(object sender, RoutedEventArgs e) => CommitThreshold();
    private void ThresholdBox_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { e.Handled = true; CommitThreshold(); } }
    private void CommitThreshold()
    {
        if (int.TryParse(ThresholdBox.Text, out var value)) ThresholdSlider.Value = Math.Clamp(value, 0, 255);
        ThresholdBox.Text = ((int)Math.Round(ThresholdSlider.Value)).ToString();
    }
    private void InvertChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _settings.Invert = InvertCheck.IsChecked == true;
        if (_trainingRecipe is not null && Recognition.ProductPipeline.RecipeKey(_trainingRecipe) != Recognition.ProductPipeline.RecipeKey(_settings)) InvalidateTrainingSample();
        ScheduleThresholdRefresh();
    }
    private void ScheduleThresholdRefresh()
    {
        _thresholdTimer.Stop();
        if (IsRecognitionModule && !_navigating && !_closing && !_busy && _automaticTask is null && _source is not null && _cameraTask is null &&
            _operation is ImageOperation.Detect or ImageOperation.Contours or ImageOperation.Morphology)
            _thresholdTimer.Start();
    }
    private async void ThresholdTimer_Tick(object? sender, EventArgs e)
    {
        _thresholdTimer.Stop();
        await ProcessAsync(_operation, false); // 阈值预览不重复计入检测记录。
    }

    private async void Match_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready()) return;
        if (_template is null) await SelectTemplateAsync();
        if (_template is not null) await ProcessAsync(ImageOperation.Match);
    }
    private async void TemplateFile_Click(object sender, RoutedEventArgs e)
    { if (Ready(false)) await SelectTemplateAsync(); }
    private async Task SelectTemplateAsync()
    {
        var dialog = new OpenFileDialog { Title = "选择特征模板（尺寸不能大于检测图像）", Filter = ImageFilter };
        if (dialog.ShowDialog(this) != true) return;
        await BusyAsync("正在加载模板……", async () =>
        {
            using var loaded = await Task.Run(() => OpenCvImageService.LoadColor(dialog.FileName));
            if (_closed || _closing) return;
            var next = loaded.Clone(); _template?.Dispose(); _template = next; _templatePath = dialog.FileName;
            StatusText.Text = $"已设置模板 · {Path.GetFileName(dialog.FileName)} · {loaded.Width} × {loaded.Height}";
        });
    }

    private async void Histogram_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready()) return;
        await BusyAsync("正在计算直方图……", async () =>
        {
            using var snapshot = (_processed?.Image ?? _source!).Clone();
            using var histogram = await Task.Run(() => ImageProcessor.Histogram(snapshot));
            if (_closed || _closing) return;
            var bitmap = histogram.Image.ToBitmapSource(); bitmap.Freeze();
            var content = new DockPanel { Margin = new Thickness(15) };
            var label = new TextBlock { Text = histogram.Description, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(label, Dock.Bottom); content.Children.Add(label);
            content.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Uniform });
            new System.Windows.Window { Owner = this, Title = "灰度直方图 · 当前显示图像", Width = 860, Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = content }.ShowDialog();
            StatusText.Text = histogram.Description;
        });
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready()) return;
        var dialog = new SaveFileDialog
        {
            Title = "保存当前显示图像", Filter = "PNG 图像|*.png|JPEG 图像|*.jpg|BMP 图像|*.bmp|TIFF 图像|*.tif",
            FileName = "result_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"), DefaultExt = ".png", AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        await BusyAsync("正在保存……", async () =>
        {
            using var snapshot = (_processed?.Image ?? _source!).Clone();
            await Task.Run(() =>
            {
                var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                if (!ImageExtensions.Contains(extension)) throw new ArgumentException("不支持该图像扩展名，请选择 PNG、JPG、BMP 或 TIFF。");
                if (!Cv2.ImEncode(extension, snapshot, out var bytes))
                    throw new IOException("图像编码失败，未保存文件。");
                File.WriteAllBytes(dialog.FileName, bytes);
            });
            if (!_closed) StatusText.Text = "保存成功 · " + dialog.FileName;
        }, finishBeforeExit: true);
    }

    private async void Settings_Click(object sender, RoutedEventArgs e) => await NavigateAsync(AppPage.Settings);

    private void Properties_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready(true, true)) return;
        var image = _processed?.Image ?? _source!;
        MessageBox.Show(this, $"来源：{_sourceName}\n原图：{_source!.Width} × {_source.Height}\n" +
            $"当前图：{image.Width} × {image.Height}\n通道：{image.Channels()}\n像素类型：{image.Type()}\n" +
            $"阈值：{_settings.Threshold} / {(_settings.Invert ? "暗色前景" : "亮色前景")}\n" +
            $"模板：{(string.IsNullOrEmpty(_templatePath) ? "未设置" : _templatePath)}",
            "图像属性", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Statistics_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var total = _records.Sum(record => record.Count);
        var empty = _records.Count(record => record.Count == 0);
        var average = _records.Count == 0 ? 0 : _records.Average(record => record.ElapsedMilliseconds);
        var last = _records.LastOrDefault();
        MessageBox.Show(this, $"导入文件：{_files.Count}\n本次会话检测次数：{_records.Count}\n累计目标数：{total}\n" +
            $"无目标次数：{empty}\n平均耗时：{average:F1} ms\n" +
            $"最近一次：{(last is null ? "暂无检测记录" : $"{last.Time:HH:mm:ss}，{last.Count} 个目标，面积 {last.TotalArea:F0} px²")}\n\n" +
            "每次点击“运行检测”会新增一条记录；重复检测同一张图也会累加。阈值自动预览不计数。可从“文件”导出 CSV。",
            "信息统计", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready(false, true)) return;
        if (_records.Count == 0) { StatusText.Text = "暂无检测记录，请先点击“运行检测”。"; return; }
        var dialog = new SaveFileDialog { Title = "导出检测记录", Filter = "CSV 文件|*.csv", FileName = "检测记录_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv" };
        if (dialog.ShowDialog(this) != true) return;
        var records = _records.ToArray();
        await BusyAsync("正在导出记录……", async () =>
        {
            await Task.Run(() =>
            {
                using var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true));
                writer.WriteLine("时间,来源,阈值,暗色前景,目标数量,总面积_px2,耗时_ms");
                foreach (var row in records)
                {
                    // 防止文件名被表格应用解释为公式。
                    var source = row.Source;
                    if (source.Length > 0 && "=+-@\t\r\n".Contains(source[0])) source = "'" + source;
                    source = "\"" + source.Replace("\"", "\"\"") + "\"";
                    writer.WriteLine($"{row.Time:yyyy-MM-dd HH:mm:ss},{source},{row.Threshold},{row.Invert},{row.Count}," +
                        row.TotalArea.ToString("F2", CultureInfo.InvariantCulture) + "," + row.ElapsedMilliseconds.ToString("F2", CultureInfo.InvariantCulture));
                }
            });
            if (!_closed) StatusText.Text = $"已导出 {records.Length} 条记录 · {dialog.FileName}";
        }, finishBeforeExit: true);
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready(false) || _cameraTask is not null) return;
        if (!await EnsureCameraSelectedAsync() || _closed || _closing) return;
        _cameraHasFrame = false;
        _cameraCts = new CancellationTokenSource();
        _cameraTask = CameraLoopAsync(_cameraCts, _settings.Copy());
        StatusText.Text = $"正在连接相机 {_settings.CameraName}……"; UpdateControls();
    }

    private async Task CameraLoopAsync(CancellationTokenSource session, Models.ProcessingSettings selection)
    {
        var token = session.Token;
        try
        {
            await Task.Run(async () =>
            {
                // 摄像头对象仅由此工作线程使用和释放，停止操作不会并发 Dispose。
                using var source = new ProductCameraSource(selection, 0, 0);
                while (!token.IsCancellationRequested)
                {
                    using var frame = source.Capture(token);
                    if (_closed || token.IsCancellationRequested) break;
                    var bitmap = frame.ToBitmapSource(); bitmap.Freeze();
                    Mat? next = frame.Clone();
                    try
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (_closed || _closing || token.IsCancellationRequested || _busy) return;
                            var firstFrame = !_cameraHasFrame;
                            _cameraHasFrame = true;
                            InvalidateTrainingSample();
                            _source?.Dispose(); _source = next; next = null;
                            _processed?.Dispose(); _processed = null; _operation = ImageOperation.Original;
                            _sourceName = $"相机 {selection.CameraName}"; PathText.Text = _sourceName;
                            PreviewImage.Source = bitmap; EmptyHint.Visibility = Visibility.Collapsed;
                            StatusText.Text = $"实时预览 · {selection.CameraName} · {_source!.Width} × {_source.Height} · 点击“采样”冻结图像";
                            if (firstFrame) UpdateControls();
                        });
                    }
                    finally { next?.Dispose(); }
                    await Task.Delay(80, token);
                }
            }, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed && !_closing) ShowError(ex); }
        finally
        {
            if (ReferenceEquals(_cameraCts, session)) { _cameraCts = null; _cameraTask = null; _cameraHasFrame = false; }
            session.Dispose(); if (!_closed) UpdateControls();
        }
    }

    private async Task StopCameraAsync()
    {
        var task = _cameraTask;
        _cameraCts?.Cancel();
        if (task is not null) await task;
    }
    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await StopCameraAsync(); if (!_closed) StatusText.Text = "采集已停止，保留最后一帧。";
    }
    private async void Sample_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var wasCamera = _cameraTask is not null;
        await StopCameraAsync(); if (_closed || _closing) return;
        if (_source is null) { StatusText.Text = "尚无图像，请先开始摄像头或打开图片。"; return; }
        if (wasCamera) _sourceName += " / 采样 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        PathText.Text = _sourceName; StatusText.Text = "采样已就绪，可检测、处理或保存当前图像。";
    }

    private async void CloseImage_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await StopCameraAsync(); if (_closed || _closing) return;
        _thresholdTimer.Stop(); InvalidateTrainingSample(); _source?.Dispose(); _source = null; _processed?.Dispose(); _processed = null;
        _operation = ImageOperation.Original; _sourceName = string.Empty;
        FilesGrid.SelectedItem = null;
        PreviewImage.Source = null; EmptyHint.Visibility = Visibility.Visible; PathText.Text = string.Empty;
        StatusText.Text = "当前图像已关闭";
        UpdateControls();
    }
    private void ClearFiles_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready(false)) return;
        _files.Clear(); SearchBox.Text = string.Empty; _search = string.Empty;
        StatusText.Text = "文件列表已清空，当前图像保留。";
        UpdateControls();
    }
    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        ImageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        ImageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled; PreviewImage.Stretch = Stretch.Uniform;
    }
    private void ActualSize_Click(object sender, RoutedEventArgs e)
    {
        ImageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        ImageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; PreviewImage.Stretch = Stretch.None;
    }
    private void Help_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "建档：拍照（或打开图片） → 设置滤波/阈值/最小面积 → 滤波+轮廓测量 → 填产品名称和保存目录 → 保存产品类。\n\n" +
        "每个产品类保存 product.json、original.png、processed.png、binary.png。面积单位 px²，周长单位 px，要求恰好一个有效外轮廓。\n\n" +
        "运行：加载所有产品类 → 填域名/IP、端口、面积/周长误差及采样频率 → 连接机器人 → 开始自动识别。\n\n" +
        "仅在面积和周长均满足允许误差、且唯一匹配一个产品类时发送 UTF-8 JSON 行报文。多个候选、不匹配或无单目标时不发送。\n\n" +
        "机器人需实现本项目协议：TCP 服务端按换行分包，读取 productType/productId。TCP 写入成功不代表机器人已执行，当前无 ACK 和自动重发。\n\n" +
        "自动识别期间可停止或断开；离开模块/退出会先停止采样。运行记录自动写入本机日志，可在停止后导出。\n\n" +
        "所有产品类须使用一致的采集分辨率、视距和照明；同一物体停留在画面中会按每个采样周期重复发送。",
        "识别与分拣模块 · 使用说明", MessageBoxButton.OK, MessageBoxImage.Information);
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            if (_closing) return;
            _closing = true; _thresholdTimer.Stop(); _robotStatusTimer.Stop();
            StatusText.Text = "正在退出 · 停止采样并保存数据……";
            _shutdown.Cancel(); _automaticCts?.Cancel(); _cameraCts?.Cancel();
            _robot.Dispose(); // 先关闭 Socket，唤醒正在等待的连接/发送，不再发送新报文。
            UpdateControls();
            var settings = _settings.Copy();
            var saveSettings = _exitCoordinator.RunWriteAsync(() => Task.Run(() =>
            {
                try { settings.Save(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            }));
            try
            {
                // 各任务同时收到取消，共用一次 1.5 秒宽限期。落盘操作不受该期限截断。
                _workersStopped = await _exitCoordinator.FinishAsync(
                    new[] { _automaticTask, _cameraTask, _busyAction, saveSettings }, TimeSpan.FromMilliseconds(1500));
            }
            finally { _allowClose = true; Close(); }
            return;
        }
        _closed = true;
        InvalidateTrainingSample();
        _source?.Dispose(); _source = null; _processed?.Dispose(); _processed = null;
        _template?.Dispose(); _template = null;
        // 超时的工作线程仍持有自己的快照与取消令牌，不与其并发释放相机或 CTS。
        if (_workersStopped) _shutdown.Dispose();
    }
}
