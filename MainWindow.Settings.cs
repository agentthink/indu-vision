using System.Windows;
using DigitalTwinIndustrialAutomationPlatform.Models;
using DigitalTwinIndustrialAutomationPlatform.Views;

namespace DigitalTwinIndustrialAutomationPlatform;

public partial class MainWindow
{
    private async void FilterSettings_Click(object sender, RoutedEventArgs e) => await ShowProcessingSettingsAsync(ProcessingSettingsSection.Filter);
    private async void EdgeSettings_Click(object sender, RoutedEventArgs e) => await ShowProcessingSettingsAsync(ProcessingSettingsSection.Edges);
    private async void ContourSettings_Click(object sender, RoutedEventArgs e) => await ShowProcessingSettingsAsync(ProcessingSettingsSection.Contours);
    private async void MorphologySettings_Click(object sender, RoutedEventArgs e) => await ShowProcessingSettingsAsync(ProcessingSettingsSection.Morphology);
    private async void MatchSettings_Click(object sender, RoutedEventArgs e) => await ShowProcessingSettingsAsync(ProcessingSettingsSection.Match);

    private async Task ShowProcessingSettingsAsync(ProcessingSettingsSection section)
    {
        if (_closed || _closing || _busy || _navigating || _automaticTask is not null || _stoppingAutomatic) return;
        try
        {
            if (IsRecognitionModule) CommitThreshold();
            _thresholdTimer.Stop();
            var dialog = new ProcessingSettingsWindow(_settings, section) { Owner = this, Icon = Icon };
            if (dialog.ShowDialog() != true || dialog.ResultSettings is not { } candidate || _closed || _closing) return;
            InvalidateTrainingSample();
            _settings = candidate;
            ThresholdSlider.Value = candidate.Threshold;
            ThresholdBox.Text = candidate.Threshold.ToString();
            InvertCheck.IsChecked = candidate.Invert;
            _thresholdTimer.Stop(); // 更新表单值不触发重复预览。
            var message = ProcessingSettingsWindow.SectionTitle(section) + "已保存，请重新测量后建档。";
            SettingsSaveStatus.Text = message; StatusText.Text = message;
            _refreshRecognitionResult = _source is not null && !IsRecognitionModule;
            UpdateControls();
            if (IsRecognitionModule && _source is not null && _cameraTask is null && _operation != ImageOperation.Original)
                await ProcessAsync(_operation, false);
        }
        catch (Exception ex) { if (!_closed && !_closing) ShowError(ex); }
    }
}
