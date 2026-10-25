using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using DigitalTwinIndustrialAutomationPlatform.Models;
using DigitalTwinIndustrialAutomationPlatform.Recognition;
using DigitalTwinIndustrialAutomationPlatform.Services;

namespace DigitalTwinIndustrialAutomationPlatform;

public partial class MainWindow
{
    private readonly ObservableCollection<ProductEntry> _products = new();
    private readonly ObservableCollection<RunRow> _runRows = new();
    private readonly TcpRobotClient _robot = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DispatcherTimer _robotStatusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private AutomationSettings _automation = AutomationSettings.Load();
    private ProductMeasurement? _trainingMeasurement;
    private Mat? _trainingOriginal;
    private ProcessingSettings? _trainingRecipe;
    private CancellationTokenSource? _automaticCts;
    private Task? _automaticTask;
    private bool _stoppingAutomatic;
    private bool _closing;
    private bool _allowClose;
    private bool _lastRobotConnected;
    private string? _runLogPath;
    private long _sampleCount, _matchCount, _sentCount;

    private void InitializeRecognition()
    {
        ProductsGrid.ItemsSource = _products; RunRecordsGrid.ItemsSource = _runRows;
        RobotHostBox.Text = _automation.Host; RobotPortBox.Text = _automation.Port.ToString();
        AreaToleranceBox.Text = _automation.AreaTolerancePercent.ToString(CultureInfo.CurrentCulture);
        PerimeterToleranceBox.Text = _automation.PerimeterTolerancePercent.ToString(CultureInfo.CurrentCulture);
        SamplingHzBox.Text = _automation.SamplingHz.ToString(CultureInfo.CurrentCulture);
        ProductDirectoryBox.Text = _automation.ProductSaveDirectory;
        _robotStatusTimer.Tick += (_, _) =>
        {
            if (_closed || _closing) return;
            var connected = _robot.IsConnected;
            if (connected != _lastRobotConnected) UpdateControls();
        };
        _robotStatusTimer.Start();
        Loaded += LoadProductIndex;
    }

    private async void LoadProductIndex(object sender, RoutedEventArgs e)
    {
        Loaded -= LoadProductIndex;
        await BusyAsync("正在恢复产品库……", async () =>
        {
            var result = await Task.Run(ProductStore.LoadIndex);
            if (_closed || _closing) return;
            foreach (var item in result.Entries)
            {
                if (_products.Any(p => p.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase))) continue;
                _products.Add(item);
            }
            StatusText.Text = result.Errors.Count == 0 ? $"产品库已就绪 · {_products.Count} 个产品类" : $"已加载 {_products.Count} 个产品类，{result.Errors.Count} 个失效记录未加载，可重新导入目录。";
        });
    }

    private void UpdateRecognitionControls()
    {
        var running = _automaticTask is not null;
        var idle = !_busy && !_navigating && !_closing && !running && !_stoppingAutomatic;
        ProductEditorPanel.IsEnabled = idle;
        ProductsGrid.IsEnabled = idle && _cameraTask is null;
        RobotEndpointPanel.IsEnabled = idle && !_robot.IsConnected;
        RunParameterPanel.IsEnabled = idle;
        _lastRobotConnected = _robot.IsConnected;
        RobotStateText.Text = _lastRobotConnected ? $"机器人已连接 · {_automation.Host}:{_automation.Port} · TCP / JSON 行报文" : "机器人未连接 · TCP / UTF-8 JSON 行报文";
        ProductLibraryStatus.Text = $"{_products.Count} 个产品类 · 面积 px² / 周长 px";
    }

    private void InvalidateTrainingSample()
    {
        _trainingMeasurement?.Dispose(); _trainingMeasurement = null;
        _trainingOriginal?.Dispose(); _trainingOriginal = null; _trainingRecipe = null;
        if (MeasurementText is not null) MeasurementText.Text = "拍照或导入单个产品，再点击“滤波 + 轮廓测量”";
    }

    private async void ImportProductImage_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready(false)) return;
        var dialog = new OpenFileDialog
        {
            Title = "选择用于产品建档的本地图像",
            Filter = ImageFilter,
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        // 复用图像解码和来源切换流程，保留电脑上的原文件，并使旧测量结果失效。
        var previousSource = _source;
        await ImportFilesAsync([dialog.FileName]);
        if (!_closed && !_closing && !ReferenceEquals(previousSource, _source) &&
            _sourceName.Equals(Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase))
            StatusText.Text = "本地图像已载入 · 调整处理参数后点击“滤波 + 轮廓测量”，即可保存为产品类。";
    }

    private async void CaptureProduct_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready(false, true)) return;
        if (!await EnsureCameraSelectedAsync()) return;
        await BusyAsync("正在拍照……", async () =>
        {
            await StopCameraAsync();
            var token = _shutdown.Token;
            var cameraSelection = _settings.Copy();
            using var photo = await Task.Run(() =>
            {
                using var source = new ProductCameraSource(cameraSelection, 0, 0);
                // 丢弃启动阶段帧，让相机曝光趋于稳定。
                for (var i = 0; i < 5; i++) { using var warmup = source.Capture(token); }
                return source.Capture(token);
            }, token);
            if (_closed || _closing) return;
            SetSource(photo, "产品拍照 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            StatusText.Text = $"拍照完成 · {photo.Width} × {photo.Height} · 下一步：滤波 + 轮廓测量";
        });
    }

    private async void MeasureProduct_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready()) return;
        CommitThreshold();
        await BusyAsync("正在滤波、提取轮廓并测量……", async () =>
        {
            using var snapshot = _source!.Clone(); var recipe = _settings.Copy();
            ProductMeasurement? measurement = await Task.Run(() => ProductPipeline.Measure(snapshot, recipe));
            try
            {
                if (_closed || _closing) return;
                var bitmap = measurement.Processed.ToBitmapSource(); bitmap.Freeze();
                InvalidateTrainingSample();
                _processed?.Dispose(); _processed = new ProcessingResult(measurement.Processed.Clone(), "产品轮廓测量");
                PreviewImage.Source = bitmap; _operation = ImageOperation.Original;
                if (!measurement.IsSingleTarget)
                {
                    MeasurementText.Text = $"有效轮廓 {measurement.Count} 个，建档要求恰好 1 个；请调整阈值和最小面积。";
                    StatusText.Text = MeasurementText.Text; return;
                }
                _trainingOriginal = snapshot.Clone(); _trainingRecipe = recipe;
                _trainingMeasurement = measurement; measurement = null;
                MeasurementText.Text = $"面积 {_trainingMeasurement.Area:F2} px² · 周长 {_trainingMeasurement.Perimeter:F2} px · {ImageAlgorithms.Summary(recipe)}";
                StatusText.Text = "测量完成 · 填写产品名称、选择目录并保存产品类";
            }
            finally { measurement?.Dispose(); }
        });
    }

    private void ChooseProductDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready(false)) return;
        var dialog = new OpenFolderDialog { Title = "选择产品类保存根目录" };
        if (dialog.ShowDialog(this) != true) return;
        ProductDirectoryBox.Text = dialog.FolderName;
        _automation.ProductSaveDirectory = dialog.FolderName;
        try { _automation.Save(); } catch (Exception ex) { ShowError(ex); }
    }

    private async void SaveProduct_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready() || _trainingMeasurement is null || _trainingOriginal is null || _trainingRecipe is null) return;
        var name = ProductNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { ShowError(new ArgumentException("请在右侧“产品类”页填写产品名称。")); return; }
        if (_products.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { ShowError(new ArgumentException("当前产品库已有同名产品类，请使用不同名称。")); return; }
        if (ProductPipeline.RecipeKey(_trainingRecipe) != ProductPipeline.RecipeKey(_settings))
        { InvalidateTrainingSample(); ShowError(new ArgumentException("处理参数已变化，请重新测量后保存。")); return; }
        if (string.IsNullOrWhiteSpace(ProductDirectoryBox.Text)) ChooseProductDirectory_Click(sender, e);
        if (string.IsNullOrWhiteSpace(ProductDirectoryBox.Text)) return;
        var root = ProductDirectoryBox.Text;
        await BusyAsync("正在保存产品类……", async () =>
        {
            using var original = _trainingOriginal.Clone();
            using var measurement = new ProductMeasurement(_trainingMeasurement.Processed.Clone(), _trainingMeasurement.Binary.Clone(),
                _trainingMeasurement.Count, _trainingMeasurement.Area, _trainingMeasurement.Perimeter);
            var recipe = _trainingRecipe.Copy();
            var saved = await Task.Run(() => ProductStore.Save(root, name, original, measurement, recipe));
            if (_closed) return;
            _products.Add(saved); ProductsGrid.SelectedItem = saved;
            var entries = _products.ToArray(); await Task.Run(() => ProductStore.SaveIndex(entries));
            StatusText.Text = "产品类已保存（参数、原图、处理图和二值图）· " + saved.DirectoryPath;
        }, finishBeforeExit: true);
    }

    private async void LoadProducts_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready(false)) return;
        var dialog = new OpenFolderDialog { Title = "选择单个产品类目录或包含多个产品类的根目录" };
        if (dialog.ShowDialog(this) != true) return;
        await BusyAsync("正在加载产品库……", async () =>
        {
            var loaded = await Task.Run(() =>
            {
                var valid = new List<ProductEntry>(); var errors = new List<string>();
                foreach (var path in ProductStore.Discover(dialog.FolderName))
                { try { valid.Add(ProductStore.Load(path)); } catch (Exception ex) { errors.Add($"{Path.GetFileName(path)}: {ex.Message}"); } }
                return (valid, errors);
            });
            if (_closed || _closing) return;
            foreach (var item in loaded.valid)
            {
                if (_products.Any(p => p.Profile.Id == item.Profile.Id)) continue;
                if (_products.Any(p => p.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase))) { loaded.errors.Add($"跳过重名产品：{item.Name}"); continue; }
                _products.Add(item);
            }
            var entries = _products.ToArray(); await _exitCoordinator.RunWriteAsync(() => Task.Run(() => ProductStore.SaveIndex(entries)));
            if (_closed || _closing) return;
            StatusText.Text = $"产品库：{_products.Count} 类 · {loaded.errors.Count} 项未导入";
            if (loaded.errors.Count > 0) MessageBox.Show(this, string.Join("\n", loaded.errors.Take(12)), "部分产品类未加载");
        });
    }

    private async void UseProduct_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready(false) || ProductsGrid.SelectedItem is not ProductEntry entry) return;
        await BusyAsync("正在载入产品样本……", async () =>
        {
            using var image = await Task.Run(() => OpenCvImageService.LoadColor(Path.Combine(entry.DirectoryPath, entry.Profile.OriginalImage)));
            if (_closed || _closing) return;
            var selectedCamera = _settings;
            _settings = entry.Profile.Recipe.Copy();
            _settings.CameraIndex = selectedCamera.CameraIndex;
            _settings.CameraDeviceId = selectedCamera.CameraDeviceId;
            _settings.CameraName = selectedCamera.CameraName;
            ThresholdSlider.Value = _settings.Threshold; ThresholdBox.Text = _settings.Threshold.ToString(); InvertCheck.IsChecked = _settings.Invert;
            SetSource(image, Path.Combine(entry.DirectoryPath, entry.Profile.OriginalImage));
            ProductNameBox.Text = entry.Name;
            StatusText.Text = entry.Profile.RequiresReenrollment
                ? "旧产品原图与参数已载入；请按灰度流程重新测量，以新名称建档并移出旧产品类，或先移出旧类再用原名称保存。"
                : "产品原图与灰度配方已载入，可重新测量；同名产品不会覆盖。";
        });
    }
    private async void RemoveProduct_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready(false) || ProductsGrid.SelectedItem is not ProductEntry entry) return;
        await BusyAsync("正在更新产品库……", async () =>
        {
            var remaining = _products.Where(p => p != entry).ToArray();
            await Task.Run(() => ProductStore.SaveIndex(remaining));
            if (_closed || _closing) return;
            _products.Remove(entry); StatusText.Text = "产品已移出当前库，磁盘上的产品类目录保留。";
        }, finishBeforeExit: true);
    }
    private void ProductsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => CommandManager.InvalidateRequerySuggested();

    private AutomationSettings ReadAutomationSettings()
    {
        var candidate = new AutomationSettings
        {
            Host = RobotHostBox.Text.Trim(), Port = int.Parse(RobotPortBox.Text),
            AreaTolerancePercent = double.Parse(AreaToleranceBox.Text), PerimeterTolerancePercent = double.Parse(PerimeterToleranceBox.Text),
            SamplingHz = double.Parse(SamplingHzBox.Text), ProductSaveDirectory = ProductDirectoryBox.Text,
            ConnectTimeoutSeconds = _automation.ConnectTimeoutSeconds, SendTimeoutSeconds = _automation.SendTimeoutSeconds
        };
        candidate.Validate(); return candidate;
    }

    private async void ConnectRobot_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready(false)) return;
        await BusyAsync("正在连接机器人……", async () =>
        {
            var candidate = ReadAutomationSettings(); candidate.Save();
            await _robot.ConnectAsync(candidate, _shutdown.Token);
            if (_closed || _closing) { _robot.Disconnect(); return; }
            _automation = candidate; StatusText.Text = $"机器人已连接 · {candidate.Host}:{candidate.Port}";
        });
    }
    private async void DisconnectRobot_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _navigating || _closing || !IsRecognitionModule) return;
        await BusyAsync("正在停止识别并断开机器人……", async () =>
        { await StopAutomaticAsync(); _robot.Disconnect(); StatusText.Text = "机器人已断开"; });
    }

    private async void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _navigating || _closing || _automaticTask is not null || !IsRecognitionModule) return;
        try
        {
            if (!_robot.IsConnected) throw new InvalidOperationException("请先连接机器人。");
            if (_products.Count == 0) throw new InvalidOperationException("请先保存或加载产品类。");
            var config = ReadAutomationSettings();
            var profiles = _products.Select(p => p.Profile).ToArray();
            ProductPipeline.ValidateRecognitionProfiles(profiles);
            if (profiles.Select(p => (p.ImageWidth, p.ImageHeight)).Distinct().Count() != 1)
                throw new ArgumentException("当前库中产品类的图像分辨率不一致，请使用同一相机及相同采集尺寸建档，或移出不同分辨率的产品类。");
            if (!await EnsureCameraSelectedAsync() || _closed || _closing) return;
            config.Save(); _automation = config;
            // 先停止手动预览，两个流程不会同时占用摄像头。
            await BusyAsync("正在切换到自动识别……", StopCameraAsync);
            if (_closed || _closing || !_robot.IsConnected) return;
            _thresholdTimer.Stop(); InvalidateTrainingSample();
            _sampleCount = _matchCount = _sentCount = 0; _runRows.Clear(); LastRobotMessage.Text = string.Empty;
            _runLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DigitalTwinIndustrialAutomationPlatform", "run-logs", $"run_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.jsonl");
            _automaticCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            _automaticTask = AutomaticLoopAsync(config, profiles, _automaticCts);
            AutoStateText.Text = $"正在运行 · 目标频率 {config.SamplingHz:F2} Hz · 停止前会持续采样与匹配";
            UpdateControls();
        }
        catch (Exception ex) { if (!_closed && !_closing) ShowError(ex); }
    }

    private async Task AutomaticLoopAsync(AutomationSettings config, ProductProfile[] profiles, CancellationTokenSource session)
    {
        try
        {
            var cameraSelection = _settings.Copy();
            await AutomaticRecognition.RunAsync(() => new ProductCameraSource(cameraSelection, profiles[0].ImageWidth, profiles[0].ImageHeight),
                profiles, config, _robot, _runLogPath!, async (sample, original, result) =>
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (_closed || _closing || _navigating) return;
                        SetSource(original, $"自动采样 #{sample.Sequence} · {sample.Time:HH:mm:ss.fff}");
                        var bitmap = result.Image.ToBitmapSource(); bitmap.Freeze();
                        _processed = new ProcessingResult(result.Image.Clone(), result.Decision.Description); PreviewImage.Source = bitmap;
                        _sampleCount++; if (sample.Decision.Status == RecognitionStatus.Recognized) _matchCount++;
                        if (sample.Sent) { _sentCount++; LastRobotMessage.Text = sample.Message; }
                        var decision = sample.Decision;
                        _runRows.Add(new RunRow(sample.Sequence, sample.Time.ToString("HH:mm:ss.fff"), decision.Product?.Name ?? "—",
                            decision.Area?.ToString("F2") ?? "—", decision.Perimeter?.ToString("F2") ?? "—", decision.Description, sample.Sent ? "已写入 TCP" : "未发送"));
                        while (_runRows.Count > 300) _runRows.RemoveAt(0);
                        MeasurementText.Text = $"面积 {decision.Area?.ToString("F2") ?? "—"} px² · 周长 {decision.Perimeter?.ToString("F2") ?? "—"} px";
                        AutoStateText.Text = $"采样 {_sampleCount} · 识别成功 {_matchCount} · 已发送 {_sentCount} · 本次耗时 {sample.ElapsedMs:F0} ms";
                        StatusText.Text = decision.Description + (sample.Sent ? " · 产品类型已写入 TCP，未确认机器人执行" : string.Empty);
                    });
                }, session.Token, _exitCoordinator);
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closed && !_closing)
            { AutoStateText.Text = "自动识别已停止 · " + ex.GetBaseException().Message; ShowError(ex); }
        }
        finally
        {
            if (ReferenceEquals(_automaticCts, session)) { _automaticCts = null; _automaticTask = null; }
            session.Dispose();
            if (!_closed && !_closing) { AutoStateText.Text += " · 已停止"; UpdateControls(); }
        }
    }
    private async Task StopAutomaticAsync()
    {
        var task = _automaticTask;
        if (task is null) return;
        _stoppingAutomatic = true; _automaticCts?.Cancel(); UpdateControls();
        try { await task; }
        finally { _stoppingAutomatic = false; if (!_closed) UpdateControls(); }
    }
    private async void AutoStop_Click(object sender, RoutedEventArgs e) => await StopAutomaticAsync();

    private async void ExportRunLog_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready(false) || _runLogPath is null || !File.Exists(_runLogPath)) return;
        var source = _runLogPath;
        var dialog = new SaveFileDialog { Title = "导出完整运行日志", Filter = "JSON Lines 日志|*.jsonl", FileName = Path.GetFileName(source) };
        if (dialog.ShowDialog(this) != true) return;
        await BusyAsync("正在导出运行日志……", async () =>
        {
            if (!Path.GetFullPath(source).Equals(Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase))
                await Task.Run(() => File.Copy(source, dialog.FileName, true));
            StatusText.Text = "完整运行日志已导出 · " + dialog.FileName;
        }, finishBeforeExit: true);
    }
}
