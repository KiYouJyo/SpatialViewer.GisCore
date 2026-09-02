namespace SpatialViewer.Gis.Core;

public readonly record struct TileCacheKey(
    string SourceKey,
    string LayerName,
    TileCoordinate Coordinate);

public sealed class TileMemoryCache
{
    private readonly object _gate = new();
    private readonly Dictionary<TileCacheKey, LinkedListNode<CacheEntry>> _entries = new();
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly long _maximumBytes;
    private long _currentBytes;

    public TileMemoryCache(long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        _maximumBytes = maximumBytes;
    }

    public long MaximumBytes => _maximumBytes;

    public long CurrentBytes
    {
        get
        {
            lock (_gate)
            {
                return _currentBytes;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public bool TryGet(TileCacheKey key, out TileReadResult? result)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                result = null;
                return false;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            result = node.Value.Result;
            return true;
        }
    }

    public void Set(TileCacheKey key, TileReadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        lock (_gate)
        {
            if (_entries.Remove(key, out var existing))
            {
                _lru.Remove(existing);
                _currentBytes -= existing.Value.SizeBytes;
            }

            var sizeBytes = result.ByteLength;
            if (sizeBytes > _maximumBytes)
            {
                return;
            }

            var entry = new CacheEntry(key, result, sizeBytes);
            var node = _lru.AddFirst(entry);
            _entries.Add(key, node);
            _currentBytes += sizeBytes;

            while (_currentBytes > _maximumBytes && _lru.Last is not null)
            {
                var last = _lru.Last;
                _lru.RemoveLast();
                _entries.Remove(last.Value.Key);
                _currentBytes -= last.Value.SizeBytes;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _lru.Clear();
            _currentBytes = 0;
        }
    }

    private sealed record CacheEntry(TileCacheKey Key, TileReadResult Result, int SizeBytes);
}

public sealed class TileRequestCoordinator : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _activeRequest;
    private bool _disposed;

    public async ValueTask<T> RunLatestAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        CancellationTokenSource requestSource;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeRequest?.Cancel();
            _activeRequest?.Dispose();
            requestSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeRequest = requestSource;
        }

        try
        {
            return await operation(requestSource.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activeRequest, requestSource))
                {
                    _activeRequest = null;
                }
            }

            requestSource.Dispose();
        }
    }

    public void CancelActive()
    {
        lock (_gate)
        {
            _activeRequest?.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _activeRequest?.Cancel();
            _activeRequest?.Dispose();
            _activeRequest = null;
        }
    }
}
