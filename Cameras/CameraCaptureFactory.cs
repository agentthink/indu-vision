using System.IO;
using System.Runtime.Versioning;
using OpenCvSharp;
using DigitalTwinIndustrialAutomationPlatform.Models;

namespace DigitalTwinIndustrialAutomationPlatform.Cameras;

[SupportedOSPlatform("windows")]
public static class CameraCaptureFactory
{
    public static VideoCapture Open(ProcessingSettings settings, int width = 0, int height = 0)
    {
        // 按稳定设备标识重新解析编号，防止插拔后把另一台相机误当作原设备。
        var device = CameraIdentity.Resolve(settings.CameraDeviceId, DirectShowCameraEnumerator.Enumerate());
        var camera = new VideoCapture(device.CaptureIndex!.Value, VideoCaptureAPIs.DSHOW);
        try
        {
            if (!camera.IsOpened()) throw new IOException($"无法连接相机“{device.Name}”，请检查驱动、隐私权限及设备占用。");
            var current = DirectShowCameraEnumerator.Enumerate().FirstOrDefault(d => d.CaptureIndex == device.CaptureIndex);
            if (current is null || !current.Id.Equals(device.Id, StringComparison.OrdinalIgnoreCase))
                throw new IOException("连接过程中设备列表发生变化，请重新刷新并选择相机。");
            if (width > 0) camera.Set(VideoCaptureProperties.FrameWidth, width);
            if (height > 0) camera.Set(VideoCaptureProperties.FrameHeight, height);
            camera.Set(VideoCaptureProperties.BufferSize, 1);
            return camera;
        }
        catch { camera.Dispose(); throw; }
    }
}
