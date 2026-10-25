using System.IO;
using System.Text.Json;
using DigitalTwinIndustrialAutomationPlatform.Models;

namespace DigitalTwinIndustrialAutomationPlatform.Recognition;

public sealed class ProductProfile
{
    public int SchemaVersion { get; set; } = 2;
    // v1 的参考特征可能来自“彩色滤波后转灰度”，不能与新流程混用。
    public string ProcessingPipeline => SchemaVersion == 2 ? "GrayscaleBeforeFilterV1" : "LegacyColorBeforeGrayscale";
    public bool RequiresReenrollment => SchemaVersion == 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public double Area { get; set; }
    public double Perimeter { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public ProcessingSettings Recipe { get; set; } = new();
    public string OriginalImage { get; set; } = "original.png";
    public string ProcessedImage { get; set; } = "processed.png";
    public string BinaryImage { get; set; } = "binary.png";
    public void Validate()
    {
        if (SchemaVersion is not (1 or 2) || Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 80 ||
            Name.Any(char.IsControl) || !double.IsFinite(Area) || Area <= 0 || !double.IsFinite(Perimeter) || Perimeter <= 0 ||
            ImageWidth <= 0 || ImageHeight <= 0 || Recipe is null ||
            OriginalImage != "original.png" || ProcessedImage != "processed.png" || BinaryImage != "binary.png")
            throw new InvalidDataException("产品类格式或测量参数无效。");
        Recipe.Validate();
    }
}
public sealed record ProductEntry(ProductProfile Profile, string DirectoryPath)
{
    public string Name => Profile.Name;
    public double Area => Profile.Area;
    public double Perimeter => Profile.Perimeter;
    public string Algorithms => Profile.RequiresReenrollment ? "旧版流程 · 请载入样本重新建档" : DigitalTwinIndustrialAutomationPlatform.Services.ImageAlgorithms.Summary(Profile.Recipe);
    public string Resolution => $"{Profile.ImageWidth} × {Profile.ImageHeight}";
}
public enum RecognitionStatus { Recognized, NoMatch, Ambiguous, InvalidTarget, ResolutionMismatch }
public sealed record RecognitionDecision(RecognitionStatus Status, ProductProfile? Product, double? Area, double? Perimeter,
    double? AreaErrorPercent, double? PerimeterErrorPercent, string Description);
public sealed record MatchCandidate(ProductProfile Profile, double Area, double Perimeter, double AreaError, double PerimeterError);

public sealed class AutomationSettings
{
    public string Host { get; set; } = "192.168.0.1";
    public int Port { get; set; } = 0;
    public double AreaTolerancePercent { get; set; } = 5;
    public double PerimeterTolerancePercent { get; set; } = 5;
    public double SamplingHz { get; set; } = 1;
    public string ProductSaveDirectory { get; set; } = string.Empty;
    public int ConnectTimeoutSeconds { get; set; } = 5;
    public int SendTimeoutSeconds { get; set; } = 3;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host) || Uri.CheckHostName(Host.Trim()) == UriHostNameType.Unknown ||
            Port is < 1 or > 65535 || !double.IsFinite(AreaTolerancePercent) || AreaTolerancePercent is < 0 or > 100 ||
            !double.IsFinite(PerimeterTolerancePercent) || PerimeterTolerancePercent is < 0 or > 100 ||
            !double.IsFinite(SamplingHz) || SamplingHz is < 0.1 or > 20 ||
            ConnectTimeoutSeconds is < 1 or > 60 || SendTimeoutSeconds is < 1 or > 60)
            throw new ArgumentException("请填写有效域名/IP（不含 http://）、端口 1～65535、误差 0～100% 和频率 0.1～20 Hz。");
    }
    private static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DigitalTwinIndustrialAutomationPlatform", "recognition-automation.json");
    public static AutomationSettings Load()
    {
        try
        {
            var settings = File.Exists(ConfigPath) ? JsonSerializer.Deserialize<AutomationSettings>(File.ReadAllText(ConfigPath)) ?? new() : new();
            settings.Validate(); return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return new(); }
    }
    public void Save()
    {
        Validate(); Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        var temporary = ConfigPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, ConfigPath, true);
    }
}
