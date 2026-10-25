using OpenCvSharp;
using DigitalTwinIndustrialAutomationPlatform.Models;

namespace DigitalTwinIndustrialAutomationPlatform.Services;

public sealed class ProcessingResult(Mat image, string description, int count = 0, double totalArea = 0) : IDisposable
{
    public Mat Image { get; } = image;
    public string Description { get; } = description;
    public int Count { get; } = count;
    public double TotalArea { get; } = totalArea;
    public void Dispose() => Image.Dispose();
}

public static class ImageProcessor
{
    public static Mat ToGray(Mat source)
    {
        if (source.Empty() || source.Depth() != MatType.CV_8U || source.Channels() is not (1 or 3 or 4))
            throw new ArgumentException("图像必须为非空的 8 位灰度、BGR 或 BGRA 图像。");
        if (source.Channels() == 1) return source.Clone();
        var gray = new Mat();
        try
        {
            Cv2.CvtColor(source, gray, source.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);
            return gray;
        }
        catch { gray.Dispose(); throw; }
    }

    public static Mat Segment(Mat source, ProcessingSettings settings)
    {
        using var gray = ToGray(source);
        var binary = new Mat();
        try
        {
            settings.Validate();
            if (settings.Segmentation == SegmentationKind.Range)
            {
                Cv2.InRange(gray, new Scalar(settings.Threshold), new Scalar(settings.ThresholdUpper), binary);
                if (settings.Invert) Cv2.BitwiseNot(binary, binary);
            }
            else Cv2.Threshold(gray, binary, settings.Segmentation == SegmentationKind.Otsu ? 0 : settings.Threshold, 255,
                (settings.Invert ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary) |
                (settings.Segmentation == SegmentationKind.Otsu ? ThresholdTypes.Otsu : (ThresholdTypes)0));
            return binary;
        }
        catch { binary.Dispose(); throw; }
    }

    public static ProcessingResult Apply(Mat source, ImageOperation operation, ProcessingSettings settings, Mat? template)
    {
        settings.Validate();
        if (operation == ImageOperation.Original) return new(source.Clone(), "原始图像");
        var output = new Mat();
        try
        {
            switch (operation)
            {
                case ImageOperation.Grayscale:
                    using (var gray = ToGray(source)) gray.CopyTo(output);
                    return new(output, "灰度图像 · 后续滤波与边缘处理统一使用灰度图");
                case ImageOperation.Filter:
                    using (var filtered = ImageAlgorithms.Filter(source, settings)) filtered.CopyTo(output);
                    return new(output, $"灰度滤波完成 · {ImageAlgorithms.FilterNames[(int)settings.Filter]}");
                case ImageOperation.Edges:
                    using (var mask = ImageAlgorithms.CreateMask(source, settings)) mask.CopyTo(output);
                    return new(output, $"边缘/分割完成 · {ImageAlgorithms.Summary(settings)}");
                case ImageOperation.Morphology:
                    using (var binary = Segment(source, settings))
                    using (var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(settings.KernelSize, settings.KernelSize)))
                    {
                        var morph = settings.Morphology switch
                        {
                            MorphKind.Close => MorphTypes.Close,
                            MorphKind.Erode => MorphTypes.Erode,
                            MorphKind.Dilate => MorphTypes.Dilate,
                            _ => MorphTypes.Open
                        };
                        Cv2.MorphologyEx(binary, output, morph, kernel);
                    }
                    return new(output, $"形态学 {settings.Morphology} · 阈值 {settings.Threshold}");
                case ImageOperation.Detect:
                case ImageOperation.Contours:
                    using (var filtered = ImageAlgorithms.Filter(source, settings))
                    using (var binary = ImageAlgorithms.Mask(filtered, settings))
                    {
                        Cv2.FindContours(binary, out Point[][] contours, out HierarchyIndex[] _,
                            RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                        Cv2.CvtColor(filtered, output, ColorConversionCodes.GRAY2BGR); // 仅为彩色标注转三通道
                        var count = 0;
                        double total = 0;
                        foreach (var contour in contours)
                        {
                            var area = Math.Abs(Cv2.ContourArea(contour));
                            if (area < settings.MinArea) continue;
                            count++;
                            total += area;
                            var rect = Cv2.BoundingRect(contour);
                            Cv2.DrawContours(output, new[] { contour }, -1, new Scalar(0, 170, 255), 2);
                            if (operation == ImageOperation.Detect)
                            {
                                Cv2.Rectangle(output, rect, new Scalar(0, 190, 0), 2);
                                Cv2.PutText(output, $"#{count} A:{area:F0}px2", new Point(rect.X, Math.Max(18, rect.Y - 8)),
                                    HersheyFonts.HersheySimplex, 0.55, new Scalar(0, 120, 0), 2);
                            }
                        }
                        return new(output, $"{(operation == ImageOperation.Detect ? "检测" : "轮廓提取")}完成 · {count} 个目标 · 总面积 {total:F0} px²", count, total);
                    }
                case ImageOperation.Match:
                    if (template is null || template.Empty()) throw new InvalidOperationException("请先选择模板图片。");
                    if (template.Width > source.Width || template.Height > source.Height)
                        throw new ArgumentException("模板尺寸不能大于待检测图像，请先裁剪合适的模板。");
                    using (var gray = ToGray(source))
                    using (var templateGray = ToGray(template))
                    using (var scores = new Mat())
                    {
                        Cv2.MeanStdDev(templateGray, out Scalar _, out Scalar deviation);
                        if (deviation.Val0 < 0.001) throw new ArgumentException("模板为纯色，无法进行相关性匹配。请选择包含产品特征的模板。");
                        Cv2.MatchTemplate(gray, templateGray, scores, TemplateMatchModes.CCoeffNormed);
                        Cv2.MinMaxLoc(scores, out double _, out double max, out Point _, out Point location);
                        var matched = max >= settings.MatchScore;
                        source.CopyTo(output);
                        var color = matched ? new Scalar(0, 190, 0) : new Scalar(0, 0, 230);
                        Cv2.Rectangle(output, new Rect(location.X, location.Y, template.Width, template.Height), color, 2);
                        Cv2.PutText(output, $"{(matched ? "MATCH" : "NO MATCH")} {max:F3}",
                            new Point(location.X, Math.Max(20, location.Y - 10)), HersheyFonts.HersheySimplex, 0.65, color, 2);
                        return new(output, $"模板匹配 · 最佳分数 {max:F3} · 合格线 {settings.MatchScore:F2} · {(matched ? "匹配通过" : "未通过")}");
                    }
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }
        catch { output.Dispose(); throw; }
    }

    public static ProcessingResult Histogram(Mat source)
    {
        using var gray = ToGray(source);
        using var histogram = new Mat();
        Cv2.CalcHist(new[] { gray }, new[] { 0 }, null, histogram, 1, new[] { 256 }, new[] { new Rangef(0, 256) });
        Cv2.MinMaxLoc(histogram, out double _, out double maximum);
        Cv2.MeanStdDev(gray, out Scalar mean, out Scalar deviation);
        var plot = new Mat(420, 820, MatType.CV_8UC3, Scalar.White);
        try
        {
            for (var i = 0; i < 256; i++)
            {
                var height = maximum <= 0 ? 0 : (int)(histogram.At<float>(i) / maximum * 310);
                Cv2.Rectangle(plot, new Point(30 + i * 3, 350 - height), new Point(32 + i * 3, 350), new Scalar(200, 110, 30), -1);
            }
            Cv2.Line(plot, new Point(30, 350), new Point(800, 350), Scalar.Black, 1);
            Cv2.PutText(plot, "0                      Grayscale intensity                      255", new Point(28, 380),
                HersheyFonts.HersheySimplex, 0.55, Scalar.Black);
            return new(plot, $"灰度直方图 · 均值 {mean.Val0:F2} · 标准差 {deviation.Val0:F2} · 像素数 {gray.Total():N0}");
        }
        catch { plot.Dispose(); throw; }
    }
}
