using System.Reflection;

namespace DigitalTwinIndustrialAutomationPlatform.Platform;

/// <summary>平台级名称统一由此提供；模块名称与平台名称分开维护。</summary>
public static class PlatformInfo
{
    public const string Name = "数字孪生工业自动化控制平台";
    // 从实际构建版本读取；发布脚本指定版本时，“关于”页自动同步。
    public static string Version => typeof(PlatformInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(PlatformInfo).Assembly.GetName().Version?.ToString(3) ?? "未知";
    public static string VersionLabel => $"软件版本：{Version}";
    public const string RecognitionSortingId = "recognition-sorting";
    public static ModuleDefinition RecognitionSorting { get; } = new(
        RecognitionSortingId,
        "识别与分拣系统",
        "产品建档、面积周长识别、定频采样与机器人 TCP 产品类型通知。",
        "\uE9D9");
}
