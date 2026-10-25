using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalTwinIndustrialAutomationPlatform.Models;

public sealed record ImageFileItem(string FullPath, string Name, long Size, DateTime Created)
{
    public string SizeText => Size < 1024 ? $"{Size} B" : Size < 1048576 ? $"{Size / 1024.0:F1} KB" : $"{Size / 1048576.0:F1} MB";
    public string CreatedText => Created.ToString("yyyy-MM-dd HH:mm:ss");
    public static ImageFileItem FromPath(string path)
    {
        var info = new FileInfo(path);
        return new(info.FullName, info.Name, info.Length, info.CreationTime);
    }
}

public enum ImageOperation { Original, Filter, Edges, Contours, Detect, Morphology, Match, Grayscale }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FilterKind { Gaussian = 0, Median = 1, Bilateral = 2, Mean, None, Sobel, Scharr, Prewitt, Roberts, Laplacian, LoG }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EdgeKind { ThresholdContour = 0, Canny, Sobel, Scharr, Prewitt, Roberts, Laplacian, LoG }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SegmentationKind { Fixed = 0, Range, Otsu }
public enum MorphKind { Open, Close, Erode, Dilate }

public sealed class ProcessingSettings
{
    public int CameraIndex { get; set; }
    public string CameraDeviceId { get; set; } = string.Empty;
    public string CameraName { get; set; } = string.Empty;
    public int Threshold { get; set; } = 160;
    public int ThresholdUpper { get; set; } = 255;
    public SegmentationKind Segmentation { get; set; } = SegmentationKind.Fixed;
    public EdgeKind Edge { get; set; } = EdgeKind.ThresholdContour;
    public int EdgeThreshold { get; set; } = 50;
    public int DerivativeKernelSize { get; set; } = 3;
    public double GaussianSigma { get; set; }
    public double BilateralSigmaColor { get; set; } = 75;
    public double BilateralSigmaSpace { get; set; } = 75;
    public bool CloseEdgeGaps { get; set; }
    public int EdgeClosingSize { get; set; } = 3;
    public bool Invert { get; set; } = true;
    public int KernelSize { get; set; } = 5;
    public int CannyLow { get; set; } = 50;
    public int CannyHigh { get; set; } = 150;
    public double MinArea { get; set; } = 300;
    public double MatchScore { get; set; } = 0.8;
    public FilterKind Filter { get; set; } = FilterKind.Gaussian;
    public MorphKind Morphology { get; set; } = MorphKind.Open;
    public ProcessingSettings Copy() => (ProcessingSettings)MemberwiseClone();
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProductIdentificationSorting", "settings.json");

    public void Validate()
    {
        if (CameraIndex < 0 || Threshold is < 0 or > 255 || KernelSize is < 3 or > 31 || KernelSize % 2 == 0 ||
            ThresholdUpper is < 0 or > 255 || (Segmentation == SegmentationKind.Range && Threshold > ThresholdUpper) ||
            EdgeThreshold is < 0 or > 255 || DerivativeKernelSize is not (3 or 5 or 7) ||
            EdgeClosingSize is < 3 or > 15 || EdgeClosingSize % 2 == 0 ||
            !double.IsFinite(GaussianSigma) || GaussianSigma is < 0 or > 100 ||
            !double.IsFinite(BilateralSigmaColor) || BilateralSigmaColor is <= 0 or > 1000 ||
            !double.IsFinite(BilateralSigmaSpace) || BilateralSigmaSpace is <= 0 or > 1000 ||
            !Enum.IsDefined(Edge) || !Enum.IsDefined(Segmentation) || CannyLow < 0 || CannyHigh > 255 || CannyLow >= CannyHigh || MinArea < 0 || !double.IsFinite(MinArea) ||
            MatchScore is < 0 or > 1 || !double.IsFinite(MatchScore) || !Enum.IsDefined(Filter) || !Enum.IsDefined(Morphology))
            throw new ArgumentException("参数无效：阈值为 0～255（区间下限≤上限，Canny 低<高），平滑核为 3～31 奇数，微分核为 3/5/7，闭运算核为 3～15 奇数；请检查 Sigma、最小面积和匹配分数。");
    }
    public static ProcessingSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new();
            var settings = JsonSerializer.Deserialize<ProcessingSettings>(File.ReadAllText(SettingsPath)) ?? new();
            settings.Validate();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return new(); }
    }
    public void Save()
    {
        Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed record DetectionRecord(DateTime Time, string Source, int Threshold, bool Invert,
    int Count, double TotalArea, double ElapsedMilliseconds);
