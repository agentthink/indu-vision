using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DigitalTwinIndustrialAutomationPlatform.Recognition;

public sealed class TcpRobotClient : IDisposable
{
    private sealed class Connection(TcpClient client)
    {
        public TcpClient Client { get; } = client;
        public CancellationTokenSource Lifetime { get; } = new();
        public volatile bool Alive = true;
    }
    private Connection? _connection;
    private int _disposed;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    public bool IsConnected => Volatile.Read(ref _connection)?.Alive == true;

    public async Task ConnectAsync(AutomationSettings settings, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        settings.Validate(); Disconnect();
        var client = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds));
        try
        {
            await client.ConnectAsync(settings.Host.Trim(), settings.Port, timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            var connection = new Connection(client);
            Interlocked.Exchange(ref _connection, connection);
            if (Volatile.Read(ref _disposed) != 0)
            { Disconnect(); connection.Lifetime.Dispose(); throw new ObjectDisposedException(nameof(TcpRobotClient)); }
            _ = MonitorConnectionAsync(connection);
        }
        catch { client.Dispose(); throw; }
    }
    private static async Task MonitorConnectionAsync(Connection connection)
    {
        try
        {
            // 消费对端数据并检测 EOF。当前不解释这些字节，不将其视为分拣完成 ACK。
            var buffer = new byte[1024];
            var stream = connection.Client.GetStream();
            while (await stream.ReadAsync(buffer, connection.Lifetime.Token).ConfigureAwait(false) != 0) { }
        }
        catch (Exception) { /* 读取错误与关闭均标记断线，由运行循环报告。 */ }
        finally
        {
            connection.Alive = false;
            connection.Client.Dispose();
            connection.Lifetime.Dispose();
        }
    }
    public async Task<string> SendAsync(Guid runId, long sequence, RecognitionDecision decision, AutomationSettings settings, CancellationToken token)
    {
        if (decision.Status != RecognitionStatus.Recognized || decision.Product is null)
            throw new ArgumentException("只能发送唯一识别成功的产品类型。");
        var message = JsonSerializer.Serialize(new
        {
            type = "product_recognized", version = 1, runId, sequence, timestamp = DateTimeOffset.UtcNow,
            productId = decision.Product.Id, productType = decision.Product.Name,
            areaPx2 = decision.Area, perimeterPx = decision.Perimeter,
            areaErrorPercent = decision.AreaErrorPercent, perimeterErrorPercent = decision.PerimeterErrorPercent
        });
        await _writeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            var connection = Volatile.Read(ref _connection);
            if (connection?.Alive != true) throw new IOException("机器人 TCP 连接已断开。");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.SendTimeoutSeconds));
            await connection.Client.GetStream().WriteAsync(Encoding.UTF8.GetBytes(message + "\n"), timeout.Token).ConfigureAwait(false);
            return message;
        }
        catch { Disconnect(); throw; }
        finally { _writeGate.Release(); }
    }
    public void Disconnect()
    {
        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection is null) return;
        connection.Alive = false;
        try { connection.Lifetime.Cancel(); } catch (ObjectDisposedException) { }
        connection.Client.Dispose();
    }
    public void Dispose() { Interlocked.Exchange(ref _disposed, 1); Disconnect(); }
}
