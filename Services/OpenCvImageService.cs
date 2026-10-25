using System.IO;
using OpenCvSharp;

namespace DigitalTwinIndustrialAutomationPlatform.Services;

/// <summary>所有返回的 Mat 均由调用方负责释放。当前流程使用 8 位 BGR 彩色图。</summary>
public static class OpenCvImageService
{
    public static Mat LoadColor(string path)
    {
        // 通过字节解码，支持中文文件名和中文目录。
        var image = Cv2.ImDecode(File.ReadAllBytes(path), ImreadModes.Color);
        if (!image.Empty())
            return image;

        image.Dispose();
        throw new InvalidDataException("无法解码该图片，请选择有效的 JPG、PNG、BMP 或 TIFF 文件。");
    }

    public static Mat Grayscale(Mat source) => ImageProcessor.ToGray(source);

    public static Mat DetectEdges(Mat source)
    {
        using var gray = Grayscale(source);
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
        var result = new Mat();
        try
        {
            Cv2.Canny(blurred, result, 50, 150);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }
}
