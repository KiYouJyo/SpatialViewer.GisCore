using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis;

/// <summary>
/// Coordinates viewport raster reads, byte-budgeted caching, and cancellation of superseded requests.
/// </summary>
public sealed class RasterViewportReader : IDisposable
{
    private const int AutoOverviewCacheLevel = -1;
    private readonly IRasterDataSourceReader _reader;
    private readonly RasterTileCache _cache;
    private readonly RasterRequestCoordinator _coordinator = new();
    private bool _disposed;

    public RasterViewportReader(IRasterDataSourceReader reader, RasterTileCache cache)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(cache);
        _reader = reader;
        _cache = cache;
    }

    public ValueTask<RasterReadResult> ReadLatestAsync(
        string path,
        string layerName,
        RasterReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
        ArgumentNullException.ThrowIfNull(request);

        var sourceKey = Path.GetFullPath(path);
        var key = new RasterTileCacheKey(
            sourceKey,
            layerName,
            AutoOverviewCacheLevel,
            request.Window,
            request.OutputWidth,
            request.OutputHeight);

        return _coordinator.RunLatestAsync(
            token => ReadOrCacheAsync(path, layerName, request, key, token),
            cancellationToken);
    }

    public void CancelActive() => _coordinator.CancelActive();

    public void ClearCache() => _cache.Clear();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _coordinator.Dispose();
    }

    private async ValueTask<RasterReadResult> ReadOrCacheAsync(
        string path,
        string layerName,
        RasterReadRequest request,
        RasterTileCacheKey key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_cache.TryGet(key, out var cached) && cached is not null)
        {
            return cached;
        }

        var result = await _reader.ReadRasterAsync(
            path,
            layerName,
            request,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        _cache.Set(key, result);
        return result;
    }
}
