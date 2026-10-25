using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;
using OpenCvSharp;
using DigitalTwinIndustrialAutomationPlatform.Services;
using DigitalTwinIndustrialAutomationPlatform.Cameras;
using DigitalTwinIndustrialAutomationPlatform.Models;

namespace DigitalTwinIndustrialAutomationPlatform.Recognition;

public interface IProductFrameSource : IDisposable { Mat Capture(CancellationToken token); }

[SupportedOSPlatform("windows")]
public sealed class ProductCameraSource : IProductFrameSource
{
    private readonly VideoCapture _camera;
    public ProductCameraSource(ProcessingSettings settings, int width, int height)
    {
        _camera = CameraCaptureFactory.Open(settings, width, height);
    }
    public Mat Capture(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var frame = new Mat();
        try
        {
            if (!_camera.Read(frame) || frame.Empty()) throw new IOException("相机未返回有效图像，请检查连接、驱动及权限。");
            token.ThrowIfCancellationRequested(); return frame;
        }
        catch { frame.Dispose(); throw; }
    }
    public void Dispose() => _camera.Dispose();
}
public sealed record AutomaticSample(Guid RunId, long Sequence, DateTimeOffset Time, RecognitionDecision Decision,
    bool Sent, string? Message, double ElapsedMs);
public sealed record RunRow(long Sequence, string Time, string Product, string Area, string Perimeter, string Result, string SendState);

public static class AutomaticRecognition
{
    public static Task RunAsync(Func<IProductFrameSource> sourceFactory, IReadOnlyList<ProductProfile> profiles,
        AutomationSettings settings, TcpRobotClient robot, string logPath,
        Func<AutomaticSample, Mat, RecognitionFrame, Task> report, CancellationToken token, ShutdownCoordinator? writes = null) => Task.Run(async () =>
    {
        settings.Validate();
        if (profiles.Count == 0) throw new ArgumentException("请先建立或加载产品类。");
        ProductPipeline.ValidateRecognitionProfiles(profiles);
        using var source = sourceFactory();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
        using var log = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };
        var runId = Guid.NewGuid(); long sequence = 0;
        var clock = Stopwatch.StartNew(); double nextDue = 0;
        var intervalMs = 1000.0 / settings.SamplingHz;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!robot.IsConnected) throw new IOException("机器人连接已断开，自动识别停止。请重新连接后再开始。");
            // 持续读帧以丢弃旧帧；只有达到采样时刻才测量和发送，不积压分析任务。
            using var frame = source.Capture(token);
            if (clock.Elapsed.TotalMilliseconds < nextDue) { await Task.Delay(1, token).ConfigureAwait(false); continue; }
            var capturedAt = DateTimeOffset.Now; var watch = Stopwatch.StartNew();
            using var result = ProductPipeline.Recognize(frame, profiles, settings, token);
            token.ThrowIfCancellationRequested();
            string? message = null;
            if (result.Decision.Status == RecognitionStatus.Recognized)
                message = await robot.SendAsync(runId, sequence + 1, result.Decision, settings, token).ConfigureAwait(false);
            var sample = new AutomaticSample(runId, ++sequence, capturedAt, result.Decision, message is not null, message, watch.Elapsed.TotalMilliseconds);
            // 每次完成的采样均落盘；UI 仅保留最近记录。
            var line = JsonSerializer.Serialize(sample);
            if (writes is null) await log.WriteLineAsync(line).ConfigureAwait(false);
            else await writes.RunWriteAsync(() => log.WriteLineAsync(line)).ConfigureAwait(false);
            await report(sample, frame, result).ConfigureAwait(false);
            nextDue += intervalMs;
            if (nextDue <= clock.Elapsed.TotalMilliseconds)
                nextDue = (Math.Floor(clock.Elapsed.TotalMilliseconds / intervalMs) + 1) * intervalMs;
        }
    }, token);
}
