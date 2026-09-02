using System.Globalization;
using SpatialViewer.Formats.Gis;
using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis.WebMap;

public sealed class WmtsTileDataSourceReader : ITileDataSourceReader, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly WebMapHttpClient _webClient;
    private readonly string _tileMatrixSet;
    private readonly string _tileMatrixTemplate;
    private readonly string _style;
    private readonly TileContentType _contentType;
    private readonly TileScheme _rowScheme;
    private readonly int _minimumZoom;
    private readonly int _maximumZoom;
    private readonly int _tileSize;
    private readonly TileMemoryCache _cache;
    private bool _disposed;

    public WmtsTileDataSourceReader(
        string tileMatrixSet,
        string tileMatrixTemplate = "{z}",
        string style = "default",
        TileContentType contentType = TileContentType.Png,
        TileScheme rowScheme = TileScheme.Xyz,
        int minimumZoom = 0,
        int maximumZoom = 22,
        int tileSize = 256,
        TimeSpan? requestTimeout = null,
        int maximumRetries = 2,
        TimeSpan? retryDelay = null,
        long cacheBytes = 64L * 1024 * 1024)
        : this(
            new HttpClient(),
            true,
            tileMatrixSet,
            tileMatrixTemplate,
            style,
            contentType,
            rowScheme,
            minimumZoom,
            maximumZoom,
            tileSize,
            requestTimeout,
            maximumRetries,
            retryDelay,
            cacheBytes)
    {
    }

    public WmtsTileDataSourceReader(
        HttpClient httpClient,
        string tileMatrixSet,
        string tileMatrixTemplate = "{z}",
        string style = "default",
        TileContentType contentType = TileContentType.Png,
        TileScheme rowScheme = TileScheme.Xyz,
        int minimumZoom = 0,
        int maximumZoom = 22,
        int tileSize = 256,
        TimeSpan? requestTimeout = null,
        int maximumRetries = 2,
        TimeSpan? retryDelay = null,
        long cacheBytes = 64L * 1024 * 1024)
        : this(
            httpClient,
            false,
            tileMatrixSet,
            tileMatrixTemplate,
            style,
            contentType,
            rowScheme,
            minimumZoom,
            maximumZoom,
            tileSize,
            requestTimeout,
            maximumRetries,
            retryDelay,
            cacheBytes)
    {
    }

    private WmtsTileDataSourceReader(
        HttpClient httpClient,
        bool ownsHttpClient,
        string tileMatrixSet,
        string tileMatrixTemplate,
        string style,
        TileContentType contentType,
        TileScheme rowScheme,
        int minimumZoom,
        int maximumZoom,
        int tileSize,
        TimeSpan? requestTimeout,
        int maximumRetries,
        TimeSpan? retryDelay,
        long cacheBytes)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(tileMatrixSet);
        ArgumentException.ThrowIfNullOrWhiteSpace(tileMatrixTemplate);
        if (!tileMatrixTemplate.Contains("{z}", StringComparison.Ordinal))
        {
            throw new ArgumentException("WMTS tile matrix template must contain {z}.", nameof(tileMatrixTemplate));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(style);
        if (contentType is not (TileContentType.Png or TileContentType.Jpeg or TileContentType.WebP or TileContentType.VectorPbf))
        {
            throw new ArgumentException("WMTS content type must be PNG, JPEG, WebP, or vector PBF.", nameof(contentType));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(minimumZoom);
        if (maximumZoom < minimumZoom || maximumZoom > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumZoom));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cacheBytes);

        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _tileMatrixSet = tileMatrixSet;
        _tileMatrixTemplate = tileMatrixTemplate;
        _style = style;
        _contentType = contentType;
        _rowScheme = rowScheme;
        _minimumZoom = minimumZoom;
        _maximumZoom = maximumZoom;
        _tileSize = tileSize;
        _cache = new TileMemoryCache(cacheBytes);
        _webClient = new WebMapHttpClient(
            httpClient,
            requestTimeout ?? TimeSpan.FromSeconds(15),
            maximumRetries,
            retryDelay ?? TimeSpan.FromMilliseconds(200));
    }

    public string FormatId => "wmts";

    public ValueTask<TileSourceMetadata> ReadMetadataAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var endpoint = WebMapUriBuilder.Build(source, Array.Empty<KeyValuePair<string, string>>());
        return ValueTask.FromResult(new TileSourceMetadata(
            endpoint.Host,
            _rowScheme,
            _minimumZoom,
            _maximumZoom,
            _tileSize,
            SpatialReference.FromEpsg(3857),
            _contentType));
    }

    public async ValueTask<TileReadResult?> ReadTileAsync(
        string source,
        string layerName,
        TileCoordinate coordinate,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
        if (!coordinate.IsValid)
        {
            throw new ArgumentException("Tile coordinate must be valid.", nameof(coordinate));
        }

        if (coordinate.Zoom < _minimumZoom || coordinate.Zoom > _maximumZoom)
        {
            return null;
        }

        var cacheKey = new TileCacheKey(source, layerName, coordinate);
        if (_cache.TryGet(cacheKey, out var cached))
        {
            return cached;
        }

        var row = _rowScheme == TileScheme.Tms ? coordinate.ToTmsRow() : coordinate.Y;
        var tileMatrix = _tileMatrixTemplate.Replace(
            "{z}",
            coordinate.Zoom.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        var uri = WebMapUriBuilder.Build(source, new[]
        {
            KeyValuePair.Create("SERVICE", "WMTS"),
            KeyValuePair.Create("REQUEST", "GetTile"),
            KeyValuePair.Create("VERSION", "1.0.0"),
            KeyValuePair.Create("LAYER", layerName),
            KeyValuePair.Create("STYLE", _style),
            KeyValuePair.Create("FORMAT", GetMimeType(_contentType)),
            KeyValuePair.Create("TILEMATRIXSET", _tileMatrixSet),
            KeyValuePair.Create("TILEMATRIX", tileMatrix),
            KeyValuePair.Create("TILEROW", row.ToString(CultureInfo.InvariantCulture)),
            KeyValuePair.Create("TILECOL", coordinate.X.ToString(CultureInfo.InvariantCulture)),
        });
        var response = await _webClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        var actualType = response.ContentType == TileContentType.Unknown ? _contentType : response.ContentType;
        var result = new TileReadResult(coordinate, actualType, response.Content)
        {
            EntityTag = response.EntityTag,
            LastModified = response.LastModified,
        };
        _cache.Set(cacheKey, result);
        return result;
    }

    public void ClearCache() => _cache.Clear();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static string GetMimeType(TileContentType contentType) => contentType switch
    {
        TileContentType.Png => "image/png",
        TileContentType.Jpeg => "image/jpeg",
        TileContentType.WebP => "image/webp",
        TileContentType.VectorPbf => "application/vnd.mapbox-vector-tile",
        _ => throw new ArgumentOutOfRangeException(nameof(contentType)),
    };
}
