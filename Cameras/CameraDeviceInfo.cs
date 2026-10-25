namespace DigitalTwinIndustrialAutomationPlatform.Cameras;

public sealed record CameraDeviceInfo(string Id, string Name, string DevicePath, int? CaptureIndex,
    string Provider, bool CanCapture, string Status)
{
    public string IndexText => CaptureIndex?.ToString() ?? "—";
}
public sealed record CameraScanResult(IReadOnlyList<CameraDeviceInfo> Devices, IReadOnlyList<string> Warnings);

public static class CameraIdentity
{
    public static string Normalize(string? path)
    {
        var value = (path ?? string.Empty).Trim().ToLowerInvariant();
        if (value.StartsWith("@device:pnp:")) value = value[12..];
        if (value.StartsWith(@"\\?\")) value = value[4..];
        value = value.Replace('#', '\\');
        var interfaceSuffix = value.IndexOf(@"\{", StringComparison.Ordinal);
        return interfaceSuffix >= 0 ? value[..interfaceSuffix] : value;
    }
    public static CameraDeviceInfo Resolve(string id, IReadOnlyList<CameraDeviceInfo> devices)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("请先在“采集 → 连接相机”中选择设备。");
        var selected = devices.FirstOrDefault(d => d.CanCapture && string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        return selected ?? throw new InvalidOperationException("所选相机未找到或已拔出，请刷新相机列表后重新选择。不会自动切换到其他设备。");
    }
    public static bool IsCameraCandidate(string name, string category, string manufacturer)
    {
        if (category.Equals("Camera", StringComparison.OrdinalIgnoreCase) || category.Equals("Image", StringComparison.OrdinalIgnoreCase)) return true;
        var description = name + " " + manufacturer;
        string[] keywords = ["camera", "webcam", "相机", "摄像", "海康机器人", "hikrobot", "basler", "大恒", "daheng", "mindvision", "迈德威视", "allied vision", "the imaging source", "ids imaging", "gige vision", "genicam", "flir", "teledyne", "point grey", "baumer", "ueye", "dalsa", "euresys", "图像采集", "工业视觉"];
        return keywords.Any(keyword => description.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
