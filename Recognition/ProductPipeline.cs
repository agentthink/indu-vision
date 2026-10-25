using System.Globalization;
using OpenCvSharp;
using DigitalTwinIndustrialAutomationPlatform.Models;
using DigitalTwinIndustrialAutomationPlatform.Services;

namespace DigitalTwinIndustrialAutomationPlatform.Recognition;

public sealed class ProductMeasurement(Mat processed, Mat binary, int count, double area, double perimeter) : IDisposable
{
    public Mat Processed { get; } = processed;
    public Mat Binary { get; } = binary;
    public int Count { get; } = count;
    public double Area { get; } = area;
    public double Perimeter { get; } = perimeter;
    public bool IsSingleTarget => Count == 1 && Area > 0 && Perimeter > 0;
    public void Dispose() { Processed.Dispose(); Binary.Dispose(); }
}

public static class ProductPipeline
{
    // 建档和运行期调用同一算法；每个产品类携带自己的处理配方。
    public static string RecipeKey(ProcessingSettings s) => string.Create(CultureInfo.InvariantCulture,
        $"v3-gray-before-filter|{s.Filter}|{s.KernelSize}|{s.GaussianSigma:R}|{s.BilateralSigmaColor:R}|{s.BilateralSigmaSpace:R}|{s.Edge}|{s.DerivativeKernelSize}|{s.EdgeThreshold}|{s.CannyLow}|{s.CannyHigh}|{s.CloseEdgeGaps}|{s.EdgeClosingSize}|{s.Segmentation}|{s.Threshold}|{s.ThresholdUpper}|{s.Invert}|{s.MinArea:R}");

    public static ProductMeasurement Measure(Mat source, ProcessingSettings settings)
    {
        settings.Validate();
        using var filtered = ImageProcessor.Apply(source, ImageOperation.Filter, settings, null);
        Mat? binary = null; Mat? annotated = null;
        try
        {
            binary = ImageAlgorithms.Mask(filtered.Image, settings);
            Cv2.FindContours(binary, out Point[][] contours, out HierarchyIndex[] _, RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);
            var valid = contours.Where(contour => Math.Abs(Cv2.ContourArea(contour)) >= settings.MinArea).ToArray();
            annotated = new Mat();
            if (filtered.Image.Channels() == 1) Cv2.CvtColor(filtered.Image, annotated, ColorConversionCodes.GRAY2BGR);
            else if (filtered.Image.Channels() == 4) Cv2.CvtColor(filtered.Image, annotated, ColorConversionCodes.BGRA2BGR);
            else filtered.Image.CopyTo(annotated);
            double area = 0, perimeter = 0;
            Cv2.DrawContours(annotated, valid, -1, new Scalar(0, 180, 0), 2);
            if (valid.Length == 1)
            {
                area = Math.Abs(Cv2.ContourArea(valid[0])); perimeter = Cv2.ArcLength(valid[0], true);
                Cv2.PutText(annotated, $"A={area:F1}px2  P={perimeter:F1}px", new Point(12, 28),
                    HersheyFonts.HersheySimplex, 0.65, new Scalar(0, 130, 0), 2);
            }
            return new(annotated, binary, valid.Length, area, perimeter);
        }
        catch { annotated?.Dispose(); binary?.Dispose(); throw; }
    }

    public static void ValidateRecognitionProfiles(IReadOnlyList<ProductProfile> profiles)
    {
        foreach (var profile in profiles)
        {
            profile.Validate();
            if (profile.RequiresReenrollment)
                throw new ArgumentException($"产品类“{profile.Name}”使用旧版处理流程。请载入样本，按灰度流程重新测量建档，再移出旧产品类后开始识别。");
        }
    }

    public static double RelativeError(double current, double reference)
    {
        if (!double.IsFinite(current) || current < 0 || !double.IsFinite(reference) || reference <= 0)
            throw new ArgumentException("特征值必须为有效非负数，参考值必须大于零。");
        return Math.Abs(current - reference) / reference * 100;
    }

    public static RecognitionDecision Decide(IReadOnlyList<MatchCandidate> matches, bool sameResolution,
        bool validTarget, double? area, double? perimeter)
    {
        if (matches.Count == 1)
        {
            var m = matches[0];
            return new(RecognitionStatus.Recognized, m.Profile, m.Area, m.Perimeter, m.AreaError, m.PerimeterError, $"识别成功：{m.Profile.Name}");
        }
        if (matches.Count > 1) return new(RecognitionStatus.Ambiguous, null, area, perimeter, null, null,
            "多个产品类同时符合误差，未发送：" + string.Join("、", matches.Select(m => m.Profile.Name)));
        return new(!sameResolution ? RecognitionStatus.ResolutionMismatch : !validTarget ? RecognitionStatus.InvalidTarget : RecognitionStatus.NoMatch,
            null, area, perimeter, null, null, !sameResolution ? "采样分辨率与产品类不一致，未发送" : !validTarget ? "无有效单目标或存在多个目标，未发送" : "未匹配到产品类，未发送");
    }

    public static RecognitionFrame Recognize(Mat source, IReadOnlyList<ProductProfile> profiles, AutomationSettings settings, CancellationToken token)
    {
        settings.Validate();
        ValidateRecognitionProfiles(profiles);
        var matches = new List<MatchCandidate>(); bool sameResolution = false, validTarget = false;
        double? area = null, perimeter = null; Mat? display = null; Mat? winner = null;
        try
        {
            foreach (var group in profiles.Where(p => p.ImageWidth == source.Width && p.ImageHeight == source.Height)
                         .GroupBy(p => RecipeKey(p.Recipe)))
            {
                token.ThrowIfCancellationRequested(); sameResolution = true;
                using var measurement = Measure(source, group.First().Recipe);
                display ??= measurement.Processed.Clone();
                if (!measurement.IsSingleTarget) continue;
                validTarget = true; area ??= measurement.Area; perimeter ??= measurement.Perimeter;
                foreach (var profile in group)
                {
                    var ae = RelativeError(measurement.Area, profile.Area);
                    var pe = RelativeError(measurement.Perimeter, profile.Perimeter);
                    if (ae <= settings.AreaTolerancePercent + 1e-9 && pe <= settings.PerimeterTolerancePercent + 1e-9)
                    {
                        matches.Add(new(profile, measurement.Area, measurement.Perimeter, ae, pe));
                        winner?.Dispose(); winner = measurement.Processed.Clone();
                    }
                }
            }
            var decision = Decide(matches, sameResolution, validTarget, area, perimeter);
            if (matches.Count == 1) { display?.Dispose(); display = winner; winner = null; }
            display ??= source.Clone();
            var frame = new RecognitionFrame(display, decision); display = null;
            return frame;
        }
        finally { display?.Dispose(); winner?.Dispose(); }
    }
}
public sealed class RecognitionFrame(Mat image, RecognitionDecision decision) : IDisposable
{
    public Mat Image { get; } = image;
    public RecognitionDecision Decision { get; } = decision;
    public void Dispose() => Image.Dispose();
}
