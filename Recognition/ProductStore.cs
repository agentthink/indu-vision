using System.IO;
using System.Text.Json;
using OpenCvSharp;
using DigitalTwinIndustrialAutomationPlatform.Models;

namespace DigitalTwinIndustrialAutomationPlatform.Recognition;

public static class ProductStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static string IndexPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DigitalTwinIndustrialAutomationPlatform", "product-library.json");

    public static ProductEntry Save(string parent, string name, Mat original, ProductMeasurement measurement, ProcessingSettings recipe)
    {
        name = name.Trim();
        if (!measurement.IsSingleTarget) throw new ArgumentException("产品建档要求画面中恰好一个有效产品轮廓。");
        var profile = new ProductProfile { Name = name, Area = measurement.Area, Perimeter = measurement.Perimeter,
            ImageWidth = original.Width, ImageHeight = original.Height, Recipe = recipe.Copy() };
        profile.Validate();
        Directory.CreateDirectory(parent);
        var safeName = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
        if (string.IsNullOrEmpty(safeName)) safeName = "product";
        var final = Path.Combine(Path.GetFullPath(parent), $"product_{safeName}_{profile.Id:N}");
        var staging = final + ".staging";
        Directory.CreateDirectory(staging);
        try
        {
            WritePng(Path.Combine(staging, profile.OriginalImage), original);
            WritePng(Path.Combine(staging, profile.ProcessedImage), measurement.Processed);
            WritePng(Path.Combine(staging, profile.BinaryImage), measurement.Binary);
            File.WriteAllText(Path.Combine(staging, "product.json"), JsonSerializer.Serialize(profile, JsonOptions));
            Directory.Move(staging, final);
            return new(profile, final);
        }
        catch { if (Directory.Exists(staging)) Directory.Delete(staging, true); throw; }
    }
    private static void WritePng(string path, Mat image)
    {
        if (!Cv2.ImEncode(".png", image, out var bytes)) throw new IOException("产品图像编码失败。");
        File.WriteAllBytes(path, bytes);
    }
    public static ProductEntry Load(string directory)
    {
        directory = Path.GetFullPath(directory);
        var path = Path.Combine(directory, "product.json");
        if (new FileInfo(path).Length > 1048576) throw new InvalidDataException("产品参数文件过大。");
        var profile = JsonSerializer.Deserialize<ProductProfile>(File.ReadAllText(path)) ?? throw new InvalidDataException("产品类为空。");
        profile.Validate();
        foreach (var file in new[] { profile.OriginalImage, profile.ProcessedImage, profile.BinaryImage })
            if (!File.Exists(Path.Combine(directory, file))) throw new InvalidDataException($"产品类缺少图像：{file}");
        return new(profile, directory);
    }
    public static IEnumerable<string> Discover(string directory)
    {
        if (File.Exists(Path.Combine(directory, "product.json"))) return new[] { Path.GetFullPath(directory) };
        return Directory.EnumerateDirectories(directory).Where(d => !d.EndsWith(".staging", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(d, "product.json"))).ToArray();
    }
    public static void SaveIndex(IEnumerable<ProductEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(IndexPath)!);
        var temporary = IndexPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries.Select(p => p.DirectoryPath).ToArray(), JsonOptions));
        File.Move(temporary, IndexPath, true);
    }
    public static (List<ProductEntry> Entries, List<string> Errors) LoadIndex()
    {
        var entries = new List<ProductEntry>(); var errors = new List<string>();
        if (!File.Exists(IndexPath)) return (entries, errors);
        try
        {
            foreach (var path in JsonSerializer.Deserialize<string[]>(File.ReadAllText(IndexPath)) ?? [])
            {
                try
                {
                    var entry = Load(path);
                    if (!entries.Any(e => e.Profile.Id == entry.Profile.Id)) entries.Add(entry);
                }
                catch (Exception ex) { errors.Add($"{path}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { errors.Add(ex.Message); }
        return (entries, errors);
    }
}
