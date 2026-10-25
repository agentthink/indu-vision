using System.Management;
using System.Runtime.Versioning;

namespace DigitalTwinIndustrialAutomationPlatform.Cameras;

[SupportedOSPlatform("windows")]
public static class CameraDiscovery
{
    public static Task<CameraScanResult> ScanAsync(CancellationToken token)
    {
        var completion = new TaskCompletionSource<CameraScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { token.ThrowIfCancellationRequested(); completion.TrySetResult(Scan(token)); }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "Camera discovery" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }
    private static CameraScanResult Scan(CancellationToken token)
    {
        var devices = new List<CameraDeviceInfo>(); var warnings = new List<string>();
        try { devices.AddRange(DirectShowCameraEnumerator.Enumerate()); }
        catch (Exception ex) { warnings.Add("视频采集设备枚举失败：" + ex.Message); }
        token.ThrowIfCancellationRequested();
        try
        {
            // 系统清单补充显示需要厂商 SDK 的候选设备；不把它们误报为可采集设备。
            using var search = new ManagementObjectSearcher("root\\CIMV2",
                "SELECT Name, PNPClass, PNPDeviceID, Manufacturer, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE Present = TRUE");
            search.Options.Timeout = TimeSpan.FromSeconds(5);
            using var results = search.Get();
            var known = devices.Select(d => CameraIdentity.Normalize(string.IsNullOrWhiteSpace(d.DevicePath) ? d.Id[6..] : d.DevicePath)).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    token.ThrowIfCancellationRequested();
                    var name = item["Name"]?.ToString() ?? string.Empty;
                    var category = item["PNPClass"]?.ToString() ?? string.Empty;
                    var manufacturer = item["Manufacturer"]?.ToString() ?? string.Empty;
                    var path = item["PNPDeviceID"]?.ToString() ?? string.Empty;
                    if (!CameraIdentity.IsCameraCandidate(name, category, manufacturer) || path.Length == 0 || known.Contains(CameraIdentity.Normalize(path))) continue;
                    if (!known.Add(CameraIdentity.Normalize(path))) continue;
                    var error = Convert.ToUInt32(item["ConfigManagerErrorCode"] ?? 0u);
                    devices.Add(new("pnp:" + path, name, path, null, "系统设备 / SDK 待接入", false,
                        error == 0 ? "未提供兼容采集接口，需 SDK / 驱动接入" : $"系统设备异常（代码 {error}），需检查驱动"));
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { warnings.Add("工业/系统设备补充清单不可用：" + ex.Message); }
        return new(devices, warnings);
    }
}
