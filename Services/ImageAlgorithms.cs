using OpenCvSharp;
using DigitalTwinIndustrialAutomationPlatform.Models;

namespace DigitalTwinIndustrialAutomationPlatform.Services;

/// <summary>建档、工具预览与自动识别共用的算法实现。微分保留浮点符号后再取幅值。</summary>
public static class ImageAlgorithms
{
    public static readonly string[] FilterNames = ["高斯平滑", "中值滤波", "双边滤波", "均值滤波", "不滤波", "Sobel（一阶）", "Scharr（一阶）", "Prewitt（一阶）", "Roberts（一阶）", "Laplacian（二阶）", "LoG（二阶）"];
    public static readonly string[] EdgeNames = ["灰度阈值轮廓", "Canny", "Sobel", "Scharr", "Prewitt", "Roberts", "Laplacian", "LoG"];
    public static string Summary(ProcessingSettings s) => $"灰度 → {FilterNames[(int)s.Filter]} → {EdgeNames[(int)s.Edge]}";

    public static Mat Filter(Mat source, ProcessingSettings s)
    {
        s.Validate();
        // 所有入口统一先灰度化；禁止先对彩色通道滤波再转灰度。
        using var gray = ImageProcessor.ToGray(source);
        if (s.Filter == FilterKind.None) return gray.Clone();
        if (s.Filter >= FilterKind.Sobel) return Derivative(gray, (EdgeKind)((int)s.Filter - (int)FilterKind.Sobel + (int)EdgeKind.Sobel), s);
        var result = new Mat();
        try
        {
            var size = new Size(s.KernelSize, s.KernelSize);
            switch (s.Filter)
            {
                case FilterKind.Gaussian: Cv2.GaussianBlur(gray, result, size, s.GaussianSigma); break;
                case FilterKind.Median: Cv2.MedianBlur(gray, result, s.KernelSize); break;
                case FilterKind.Bilateral:
                    Cv2.BilateralFilter(gray, result, s.KernelSize, s.BilateralSigmaColor, s.BilateralSigmaSpace);
                    break;
                case FilterKind.Mean: Cv2.Blur(gray, result, size); break;
                default: throw new ArgumentOutOfRangeException(nameof(s.Filter));
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    // 不按每幅图的最大值归一化：同样的阈值在建档与采样中保持同样含义。
    private static Mat Derivative(Mat source, EdgeKind kind, ProcessingSettings s)
    {
        using var gray = ImageProcessor.ToGray(source);
        using var x = new Mat(); using var y = new Mat(); using var magnitude = new Mat();
        var result = new Mat();
        try
        {
            switch (kind)
            {
                case EdgeKind.Sobel:
                    Cv2.Sobel(gray, x, MatType.CV_32F, 1, 0, s.DerivativeKernelSize);
                    Cv2.Sobel(gray, y, MatType.CV_32F, 0, 1, s.DerivativeKernelSize); break;
                case EdgeKind.Scharr:
                    Cv2.Scharr(gray, x, MatType.CV_32F, 1, 0);
                    Cv2.Scharr(gray, y, MatType.CV_32F, 0, 1); break;
                case EdgeKind.Prewitt:
                case EdgeKind.Roberts:
                    using (var kx = kind == EdgeKind.Prewitt ? Mat.FromArray(new float[,] { { -1, 0, 1 }, { -1, 0, 1 }, { -1, 0, 1 } }) : Mat.FromArray(new float[,] { { 1, 0 }, { 0, -1 } }))
                    using (var ky = kind == EdgeKind.Prewitt ? Mat.FromArray(new float[,] { { -1, -1, -1 }, { 0, 0, 0 }, { 1, 1, 1 } }) : Mat.FromArray(new float[,] { { 0, 1 }, { -1, 0 } }))
                    {
                        Cv2.Filter2D(gray, x, MatType.CV_32F, kx);
                        Cv2.Filter2D(gray, y, MatType.CV_32F, ky);
                    }
                    break;
                case EdgeKind.Laplacian:
                    Cv2.Laplacian(gray, x, MatType.CV_32F, s.DerivativeKernelSize);
                    Cv2.ConvertScaleAbs(x, result); return result;
                case EdgeKind.LoG:
                    using (var blurred = new Mat())
                    {
                        Cv2.GaussianBlur(gray, blurred, new Size(s.KernelSize, s.KernelSize), s.GaussianSigma);
                        Cv2.Laplacian(blurred, x, MatType.CV_32F, s.DerivativeKernelSize);
                    }
                    Cv2.ConvertScaleAbs(x, result); return result;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            Cv2.Magnitude(x, y, magnitude);
            Cv2.ConvertScaleAbs(magnitude, result);
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    /// <summary>输入已滤波图；阈值分割或边缘算子生成用于轮廓提取的二值掩膜。</summary>
    public static Mat Mask(Mat filtered, ProcessingSettings s)
    {
        s.Validate();
        Mat? result = null;
        try
        {
            if (s.Edge == EdgeKind.ThresholdContour) result = ImageProcessor.Segment(filtered, s);
            else if (s.Edge == EdgeKind.Canny)
            {
                using var gray = ImageProcessor.ToGray(filtered);
                result = new Mat();
                Cv2.Canny(gray, result, s.CannyLow, s.CannyHigh, s.DerivativeKernelSize, true);
            }
            else
            {
                using var gradient = Derivative(filtered, s.Edge, s);
                result = new Mat();
                Cv2.Threshold(gradient, result, s.EdgeThreshold, 255, ThresholdTypes.Binary);
            }
            if (s.Edge != EdgeKind.ThresholdContour && s.CloseEdgeGaps)
            {
                using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(s.EdgeClosingSize, s.EdgeClosingSize));
                Cv2.MorphologyEx(result, result, MorphTypes.Close, kernel);
            }
            return result;
        }
        catch { result?.Dispose(); throw; }
    }

    public static Mat CreateMask(Mat source, ProcessingSettings s)
    {
        using var filtered = Filter(source, s);
        return Mask(filtered, s);
    }
}
