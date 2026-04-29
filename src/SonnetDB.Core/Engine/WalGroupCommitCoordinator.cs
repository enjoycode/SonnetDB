using SonnetDB.Wal;

namespace SonnetDB.Engine;

internal sealed class WalGroupCommitCoordinator : IDisposable
{
    private readonly object _sync = new();
    private readonly bool _enabled;
    private readonly TimeSpan _flushWindow;

    private TaskCompletionSource? _pendingCompletion;
    private bool _disposed;

    public WalGroupCommitCoordinator(WalGroupCommitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.FlushWindow < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.FlushWindow), "WAL group-commit 窗口不能为负数。");

        _enabled = options.Enabled;
        _flushWindow = options.FlushWindow;
    }

    public WalGroupCommitTicket Prepare(WalSegmentSet walSet)
    {
        ArgumentNullException.ThrowIfNull(walSet);

        if (!_enabled || _flushWindow == TimeSpan.Zero)
        {
            lock (_sync)
                ThrowIfDisposed();
            walSet.Sync();
            return default;
        }

        Task task;
        bool shouldSchedule = false;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_pendingCompletion is null)
            {
                _pendingCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                shouldSchedule = true;
            }

            task = _pendingCompletion.Task;
        }

        if (shouldSchedule)
            _ = FlushAfterDelayAsync(walSet);

        return new WalGroupCommitTicket(task);
    }

    public void FlushPending(WalSegmentSet walSet)
    {
        ArgumentNullException.ThrowIfNull(walSet);
        if (!_enabled || _flushWindow == TimeSpan.Zero)
            return;

        CompletePending(walSet);
    }

    public void Dispose()
    {
        TaskCompletionSource? pending;
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            pending = _pendingCompletion;
            _pendingCompletion = null;
        }

        pending?.TrySetException(new ObjectDisposedException(nameof(WalGroupCommitCoordinator)));
    }

    private async Task FlushAfterDelayAsync(WalSegmentSet walSet)
    {
        await Task.Delay(_flushWindow).ConfigureAwait(false);
        CompletePending(walSet);
    }

    private void CompletePending(WalSegmentSet walSet)
    {
        TaskCompletionSource? completion;
        lock (_sync)
        {
            completion = _pendingCompletion;
            _pendingCompletion = null;
        }

        if (completion is null)
            return;

        try
        {
            walSet.Sync();
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WalGroupCommitCoordinator));
    }
}

internal readonly struct WalGroupCommitTicket
{
    private readonly Task? _completion;

    public WalGroupCommitTicket(Task completion)
    {
        _completion = completion;
    }

    public void Wait()
    {
        _completion?.GetAwaiter().GetResult();
    }
}
