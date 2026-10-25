namespace DigitalTwinIndustrialAutomationPlatform.Services;

/// <summary>普通后台任务共用退出宽限期；已开始的持久化操作必须完成。</summary>
public sealed class ShutdownCoordinator
{
    private readonly object _gate = new();
    private readonly HashSet<Task> _writes = [];
    private bool _sealed;

    public async Task RunWriteAsync(Func<Task> write)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_sealed) throw new OperationCanceledException("程序正在退出，不再启动新的写入。");
            _writes.Add(finished.Task);
        }
        try { await write().ConfigureAwait(false); }
        finally
        {
            lock (_gate) { _writes.Remove(finished.Task); finished.TrySetResult(); }
        }
    }

    public async Task<bool> FinishAsync(IEnumerable<Task?> workers, TimeSpan grace)
    {
        var all = Task.WhenAll(workers.Where(t => t is not null).Select(t => ObserveAsync(t!)));
        var stopped = true;
        try { await all.WaitAsync(grace); }
        catch (TimeoutException) { stopped = false; } // 原生取帧可阻塞，不在 UI 线程并发释放设备。
        Task[] writes;
        lock (_gate) { _sealed = true; writes = _writes.ToArray(); }
        await Task.WhenAll(writes);
        return stopped;
    }

    private static async Task ObserveAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception) { /* 原任务持有自己的错误处理；退出阶段只观察其完成。 */ }
    }
}
