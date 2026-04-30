using SonnetDB.Storage.Segments;

namespace SonnetDB.Engine;

internal sealed class SegmentManagerSnapshot
{
    private int _leaseCount;
    private int _retired;
    private int _disposed;
    private IReadOnlyList<SegmentReader> _readersToDispose = Array.Empty<SegmentReader>();

    public SegmentManagerSnapshot(
        MultiSegmentIndex index,
        IReadOnlyList<SegmentReader> readers)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(readers);

        Index = index;
        Readers = readers;
    }

    public MultiSegmentIndex Index { get; }

    public IReadOnlyList<SegmentReader> Readers { get; }

    public bool TryAcquire()
    {
        while (true)
        {
            if (Volatile.Read(ref _retired) != 0)
                return false;

            int current = Volatile.Read(ref _leaseCount);
            if (Interlocked.CompareExchange(ref _leaseCount, current + 1, current) != current)
                continue;

            if (Volatile.Read(ref _retired) == 0)
                return true;

            Release();
            return false;
        }
    }

    public void Release()
    {
        int count = Interlocked.Decrement(ref _leaseCount);
        if (count == 0 && Volatile.Read(ref _retired) != 0)
            DisposeRetiredReaders();
    }

    public void Retire(IReadOnlyList<SegmentReader> readersToDispose)
    {
        ArgumentNullException.ThrowIfNull(readersToDispose);

        _readersToDispose = readersToDispose;
        if (Interlocked.Exchange(ref _retired, 1) == 0
            && Volatile.Read(ref _leaseCount) == 0)
        {
            DisposeRetiredReaders();
        }
    }

    private void DisposeRetiredReaders()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var reader in _readersToDispose)
        {
            try { reader.Dispose(); } catch { }
        }
    }
}

internal readonly struct SegmentManagerSnapshotLease : IDisposable
{
    public SegmentManagerSnapshotLease(SegmentManagerSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public SegmentManagerSnapshot Snapshot { get; }

    public void Dispose()
    {
        Snapshot.Release();
    }
}
