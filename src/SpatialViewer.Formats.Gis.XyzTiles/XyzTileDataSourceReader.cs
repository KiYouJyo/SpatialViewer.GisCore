using System.Net;
using System.Net.Http.Headers;
using SpatialViewer.Formats.Gis;
using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis.XyzTiles;

public sealed class XyzTileDataSourceReader : ITileDataSourceReader, IDisposable
{
    private const long DefaultCacheBytes = 64L * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly TileScheme _scheme;
    private readonly int _minimumZoom;
    private readonly int _maximumZoom;
    private readonly int _tileSize;
    private readonly TimeSpan _requestTimeout;
    private readonly int _maximumRetries;
    private readonly TimeSpan _retryDelay;
    private readonly TileMemoryCache _cache;
    private bool _disposed;

    public XyzTileDataSourceReader(
        TileScheme scheme = TileScheme.Xyz,
        int minimumZoom = 0,
        int maximumZoom = 22,
        int tileSize = 256,
        TimeSpan? requestTimeout = null,
        int maximumRetries = 2,
        TimeSpan? retryDelay = null,
        long cacheBytes = DefaultCacheBytes)
        : this(
            new HttpClient(),
            ownsHttpClient: true,
            scheme,
            minimumZoom,
            maximumZoom,
            tileSize,
            requestTimeout,
            maximumRetries,
            retryDelay,
            cacheBytes)
    {
    }

    public XyzTileDataSourceReader(
        HttpClient httpClient,
        TileScheme scheme = TileScheme.Xyz,
        int minimumZoom = 0,
        int maximumZoom = 22,
        int tileSize = 256,
        TimeSpan? requestTimeout = null,
        int maximumRetries = 2,
        TimeSpan? retryDelay = null,
        long cacheBytes = DefaultCacheBytes)
        : this(
            httpClient,
            ownsHttpClient: false,
            scheme,
            minimumZoom,
            maximumZoom,
            tileSize,
            requestTimeout,
            maximumRetries,
            retryDelay,
            cacheBytes)
    {
    }

    private XyzTileDataSourceReader(
        HttpClient httpClient,
        bool ownsHttpClient,
        TileScheme scheme,
        int minimumZoom,
        int maximumZoom,
        int tileSize,
        TimeSpan? requestTimeout,
        int maximumRetries,
        TimeSpan? retryDelay,
        long cacheBytes)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumZoom);
        if (maximumZoom < minimumZoom || maximumZoom > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumZoom));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileSize);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRetries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cacheBytes);

        var effectiveTimeout = requestTimeout ?? TimeSpan.FromSeconds(15);
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        var effectiveRetryDelay = retryDelay ?? TimeSpan.FromMilliseconds(200);
        if (effectiveRetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        }

        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _scheme = scheme;
        _minimumZoom = minimumZoom;
        _maximumZoom = maximumZoom;
        _tileSize = tileSize;
        _requestTimeout = effectiveTimeout;
        _maximumRetries = maximumRetries;
        _retryDelay = effectiveRetryDelay;
        _cache = new TileMemoryCache(cacheBytes);
    }

    public string FormatId => _scheme == TileScheme.Xyz ? "xyz" : "tms";

    public ValueTask<TileSourceMetadata> ReadMetadataAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var uri = ValidateTemplate(source);
        var contentType = InferContentTypeFromPath(uri.AbsolutePath);
        var metadata = new TileSourceMetadata(
            uri.Host,
            _scheme,
            _minimumZoom,
            _maximumZoom,
            _tileSize,
            SpatialReference.FromEpsg(3857),
            contentType);
        return ValueTask.FromResult(metadata);
    }

    public async ValueTask<TileReadResult?> ReadTileAsync(
        string source,
        string layerName,
        TileCoordinate coordinate,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
        if (!string.Equals(layerName, "tiles", StringComparison.Ordinal))
        {
            throw new ArgumentException($"HTTP tile layer '{layerName}' does not exist. Expected 'tiles'.", nameof(layerName));
        }

        if (!coordinate.IsValid)
        {
            throw new ArgumentException("Tile coordinate must be valid.", nameof(coordinate));
        }

        if (coordinate.Zoom < _minimumZoom || coordinate.Zoom > _maximumZoom)
        {
            return null;
        }

        var templateUri = ValidateTemplate(source);
        var cacheKey = new TileCacheKey(source, layerName, coordinate);
        if (_cache.TryGet(cacheKey, out var cached))
        {
            return cached;
        }

        var requestUri = BuildTileUri(templateUri, coordinate);
        for (var attempt = 0; attempt <= _maximumRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var requestTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeoutSource.CancelAfter(_requestTimeout);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SpatialViewer.GisCore", "0.4"));
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestTimeoutSource.Token).ConfigureAwait(false);

                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
                {
                    return null;
                }

                if (IsTransientStatus(response.StatusCode) && attempt < _maximumRetries)
                {
                    await DelayBeforeRetryAsync(response, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsByteArrayAsync(requestTimeoutSource.Token).ConfigureAwait(false);
                if (content.Length == 0)
                {
                    throw new InvalidDataException($"HTTP tile '{requestUri}' returned an empty payload.");
                }

                var contentType = ParseMediaType(response.Content.Headers.ContentType?.MediaType);
                if (contentType == TileContentType.Unknown)
                {
                    contentType = InferContentTypeFromPath(requestUri.AbsolutePath);
                }

                if (contentType == TileContentType.Unknown)
                {
                    contentType = DetectContentType(content);
                }

                var result = new TileReadResult(coordinate, contentType, content)
                {
                    EntityTag = response.Headers.ETag?.Tag,
                    LastModified = response.Content.Headers.LastModified ?? response.Headers.Date,
                };
                _cache.Set(cacheKey, result);
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && requestTimeoutSource.IsCancellationRequested)
            {
                if (attempt < _maximumRetries)
                {
                    await DelayBeforeRetryAsync(null, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new TimeoutException($"HTTP tile request '{requestUri}' exceeded {_requestTimeout}.");
            }
            catch (HttpRequestException) when (attempt < _maximumRetries)
            {
                await DelayBeforeRetryAsync(null, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("HTTP tile retry loop terminated unexpectedly.");
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

    private static Uri ValidateTemplate(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!source.Contains("{z}", StringComparison.Ordinal) ||
            !source.Contains("{x}", StringComparison.Ordinal) ||
            !source.Contains("{y}", StringComparison.Ordinal))
        {
            throw new ArgumentException("HTTP tile template must contain {z}, {x}, and {y} placeholders.", nameof(source));
        }

        var probe = source
            .Replace("{z}", "0", StringComparison.Ordinal)
            .Replace("{x}", "0", StringComparison.Ordinal)
            .Replace("{y}", "0", StringComparison.Ordinal);
        if (!Uri.TryCreate(probe, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("HTTP tile template must resolve to an absolute HTTP or HTTPS URI.", nameof(source));
        }

        return new Uri(source, UriKind.Absolute);
    }

    private Uri BuildTileUri(Uri templateUri, TileCoordinate coordinate)
    {
        var y = _scheme == TileScheme.Tms ? coordinate.ToTmsRow() : coordinate.Y;
        var text = templateUri.OriginalString
            .Replace("{z}", coordinate.Zoom.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{x}", coordinate.X.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{y}", y.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return new Uri(text, UriKind.Absolute);
    }

    private async ValueTask DelayBeforeRetryAsync(
        HttpResponseMessage? response,
        CancellationToken cancellationToken)
    {
        var delay = response?.Headers.RetryAfter?.Delta ?? _retryDelay;
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.RequestTimeout or
        HttpStatusCode.TooManyRequests or
        HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;

    private static TileContentType ParseMediaType(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "image/png" => TileContentType.Png,
        "image/jpeg" or "image/jpg" => TileContentType.Jpeg,
        "image/webp" => TileContentType.WebP,
        "application/vnd.mapbox-vector-tile" or "application/x-protobuf" or "application/protobuf" => TileContentType.VectorPbf,
        _ => TileContentType.Unknown,
    };

    private static TileContentType InferContentTypeFromPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.ToLowerInvariant() switch
        {
            ".png" => TileContentType.Png,
            ".jpg" or ".jpeg" => TileContentType.Jpeg,
            ".webp" => TileContentType.WebP,
            ".pbf" or ".mvt" => TileContentType.VectorPbf,
            _ => TileContentType.Unknown,
        };
    }

    private static TileContentType DetectContentType(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 8 &&
            content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47)
        {
            return TileContentType.Png;
        }

        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return TileContentType.Jpeg;
        }

        if (content.Length >= 12 &&
            content[0] == (byte)'R' && content[1] == (byte)'I' && content[2] == (byte)'F' && content[3] == (byte)'F' &&
            content[8] == (byte)'W' && content[9] == (byte)'E' && content[10] == (byte)'B' && content[11] == (byte)'P')
        {
            return TileContentType.WebP;
        }

        return TileContentType.Unknown;
    }
}
