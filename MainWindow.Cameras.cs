using System.Windows;
using DigitalTwinIndustrialAutomationPlatform.Cameras;
using DigitalTwinIndustrialAutomationPlatform.Views;

namespace DigitalTwinIndustrialAutomationPlatform;

public partial class MainWindow
{
    private bool HasSelectedCamera => !string.IsNullOrWhiteSpace(_settings.CameraDeviceId);

    private async Task<bool> ChooseCameraAsync()
    {
        if (_closed || _closing || _busy || _navigating || _automaticTask is not null || !IsRecognitionModule) return false;
        var dialog = new CameraConnectionWindow(_settings.CameraDeviceId) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedDevice is not { CanCapture: true } device || _closing) return false;
        bool selected = false;
        await BusyAsync("正在切换所选相机……", async () =>
        {
            await StopCameraAsync();
            var candidate = _settings.Copy();
            candidate.CameraDeviceId = device.Id; candidate.CameraName = device.Name; candidate.CameraIndex = device.CaptureIndex!.Value;
            candidate.Save(); _settings = candidate; selected = true;
            StatusText.Text = "已选择相机 · " + device.Name;
        });
        return selected;
    }
    private Task<bool> EnsureCameraSelectedAsync() => HasSelectedCamera ? Task.FromResult(true) : ChooseCameraAsync();

    private async void ConnectCamera_Click(object sender, RoutedEventArgs e)
    {
        if (await ChooseCameraAsync()) Start_Click(sender, e);
    }
    private async void DisconnectCamera_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _closing || _busy || _navigating || !IsRecognitionModule) return;
        await BusyAsync("正在停止采集并断开相机……", async () =>
        {
            await StopAutomaticAsync(); await StopCameraAsync();
            StatusText.Text = "相机采集已断开，保留最后一帧和所选设备。";
        });
    }
    private void UpdateCameraStatus()
    {
        CameraStateText.Text = !HasSelectedCamera ? "相机未选择 · 请使用“采集 → 连接相机”搜索设备"
            : _automaticTask is not null ? $"采样任务 · {_settings.CameraName}（采集状态见运行记录）"
            : _cameraTask is not null ? (_cameraHasFrame ? $"相机已连接 · {_settings.CameraName} · 预览中" : $"正在连接 · {_settings.CameraName}")
            : $"已选择 · {_settings.CameraName} · 当前未占用设备";
    }
}
