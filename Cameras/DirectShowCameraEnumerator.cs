using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace DigitalTwinIndustrialAutomationPlatform.Cameras;

/// <summary>只枚举元数据，不逐个打开设备。顺序与 OpenCV DirectShow 后端编号一致。</summary>
[SupportedOSPlatform("windows")]
public static class DirectShowCameraEnumerator
{
    private static readonly Guid SystemDeviceEnum = new("62BE5D10-60EB-11D0-BD3B-00A0C911CE86");
    private static readonly Guid VideoInputCategory = new("860BB310-5D01-11D0-BD3B-00A0C911CE86");
    private static readonly Guid PropertyBagId = new("55272A00-42CB-11CE-8135-00AA004BB851");

    public static IReadOnlyList<CameraDeviceInfo> Enumerate()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("相机枚举需要 Windows。");
        object? owner = null; IEnumMoniker? monikers = null;
        var devices = new List<CameraDeviceInfo>();
        try
        {
            owner = Activator.CreateInstance(Type.GetTypeFromCLSID(SystemDeviceEnum, true)!);
            var category = VideoInputCategory;
            var status = ((ICreateDevEnum)owner!).CreateClassEnumerator(ref category, out monikers, 0);
            if (status == 1 || monikers is null) return devices;
            Marshal.ThrowExceptionForHR(status);
            var item = new IMoniker[1]; int index = 0;
            while (monikers.Next(1, item, IntPtr.Zero) == 0)
            {
                var moniker = item[0]; object? bagObject = null;
                string display = string.Empty, name = string.Empty, path = string.Empty;
                try
                {
                    try { moniker.GetDisplayName(null!, null!, out display); } catch (COMException) { }
                    try
                    {
                        var iid = PropertyBagId;
                        moniker.BindToStorage(null!, null!, ref iid, out bagObject);
                        var bag = (IPropertyBag)bagObject;
                        name = Read(bag, "FriendlyName"); path = Read(bag, "DevicePath");
                        if (string.IsNullOrWhiteSpace(name)) name = Read(bag, "Description");
                    }
                    catch (COMException) { }
                    var stable = !string.IsNullOrWhiteSpace(path) ? path : display;
                    var identifiable = !string.IsNullOrWhiteSpace(stable);
                    devices.Add(new("dshow:" + (identifiable ? stable : $"unidentified:{index}"),
                        string.IsNullOrWhiteSpace(name) ? $"视频采集设备 #{index}" : name,
                        path, index, "DirectShow", identifiable,
                        identifiable ? "可选择连接（连接时验证采集）" : "设备标识不可读，无法可靠选择"));
                }
                finally { Release(bagObject); Release(moniker); }
                index++; // 不因元数据读取失败而压缩设备编号。
            }
            return devices;
        }
        finally { Release(monikers); Release(owner); }
    }
    private static string Read(IPropertyBag bag, string key) => bag.Read(key, out var value, IntPtr.Zero) >= 0 ? value?.ToString() ?? string.Empty : string.Empty;
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig] int CreateClassEnumerator([In] ref Guid category, [MarshalAs(UnmanagedType.Interface)] out IEnumMoniker? enumerator, int flags);
    }
    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig] int Read([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Struct)] out object? value, IntPtr errorLog);
        [PreserveSig] int Write([MarshalAs(UnmanagedType.LPWStr)] string name, [In, MarshalAs(UnmanagedType.Struct)] ref object value);
    }
}
