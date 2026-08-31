using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis;

public sealed class TileViewportReader : IDisposable
{
    private readonly TileMemoryCache _cache;
    private readonly TileRequestCoordinator _coordinator = new();
    private bool _disposed;

    public TileViewportReader(TileMemoryCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
    }

    public ValueTask<TileReadResult?> ReadLatestAsync(
        ITileDataSourceReader reader,
        string source,
        string layerName,
        TileCoordinate coordinate,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
        if (!coordinate.IsValid)
        {
            throw new ArgumentException("Tile coordinate must be valid.", nameof(coordinate));
        }

        var key = new TileCacheKey(source, layerName, coordinate);
        if (_cache.TryGet(key, out var cached))
        {
            return ValueTask.FromResult<TileReadResult?>(cached);
        }

        return _coordinator.RunLatestAsync<TileReadResult?>(async token =>
        {
            var result = await reader.ReadTileAsync(source, layerName, coordinate, token).ConfigureAwait(false);
            if (result is not null)
            {
                _cache.Set(key, result);
            }

            return result;
        }, cancellationToken);
    }

    public void CancelActive() => _coordinator.CancelActive();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _coordinator.Dispose();
    }
}
