using System.Globalization;
using SpatialViewer.Formats.Gis;
using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis.WebMap;

public sealed class WmsMapDataSourceReader : IMapImageDataSourceReader, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly WebMapHttpClient _webClient;
    private readonly string _style;
    private readonly bool _transparent;
    private bool _disposed;

    public WmsMapDataSourceReader(
        string style = "",
        bool transparent = true,
        TimeSpan? requestTimeout = null,
        int maximumRetries = 2,
        TimeSpan? retryDelay = null)
        : this(new HttpClient(), true, style, transparent, requestTimeout, maximumRetries, retryDelay)
    {
    }

    public WmsMapDataSourceReader(
        HttpClient httpClient,
        string style = "",
        bool transparent = true,
        TimeSpan? requestTimeout = null,
        int maximumRetries = 2,
        TimeSpan? retryDelay = null)
        : this(httpClient, false, style, transparent, requestTimeout, maximumRetries, retryDelay)
    {
    }

    private WmsMapDataSourceReader(
        HttpClient httpClient,
        bool ownsHttpClient,
        string style,
        bool transparent,
        TimeSpan? requestTimeout,
        int maximumRetries,
        TimeSpan? retryDelay)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _style = style ?? string.Empty;
        _transparent = transparent;
        _webClient = new WebMapHttpClient(
            httpClient,
            requestTimeout ?? TimeSpan.FromSeconds(15),
            maximumRetries,
            retryDelay ?? TimeSpan.FromMilliseconds(200));
    }

    public string FormatId => "wms";

    public async ValueTask<MapImageResult> ReadMapAsync(
        string source,
        string layerName,
        MapImageRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
        ArgumentNullException.ThrowIfNull(request);
        var crs = GetCrsIdentifier(request.SpatialReference);
        var bbox = FormatBbox(request.Bounds, request.SpatialReference);
        var requestedContentType = request.ContentType;
        var uri = WebMapUriBuilder.Build(source, new[]
        {
            KeyValuePair.Create("SERVICE", "WMS"),
            KeyValuePair.Create("REQUEST", "GetMap"),
            KeyValuePair.Create("VERSION", "1.3.0"),
            KeyValuePair.Create("LAYERS", layerName),
            KeyValuePair.Create("STYLES", _style),
            KeyValuePair.Create("CRS", crs),
            KeyValuePair.Create("BBOX", bbox),
            KeyValuePair.Create("WIDTH", request.Width.ToString(CultureInfo.InvariantCulture)),
            KeyValuePair.Create("HEIGHT", request.Height.ToString(CultureInfo.InvariantCulture)),
            KeyValuePair.Create("FORMAT", GetMimeType(requestedContentType)),
            KeyValuePair.Create("TRANSPARENT", _transparent ? "TRUE" : "FALSE"),
        });
        var response = await _webClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        var actualContentType = response.ContentType == TileContentType.Unknown
            ? requestedContentType
            : response.ContentType;
        if (actualContentType is not (TileContentType.Png or TileContentType.Jpeg or TileContentType.WebP))
        {
            throw new InvalidDataException($"WMS GetMap returned unsupported content type '{actualContentType}'.");
        }

        return new MapImageResult(request, actualContentType, response.Content)
        {
            EntityTag = response.EntityTag,
            LastModified = response.LastModified,
        };
    }

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

    private static string GetCrsIdentifier(SpatialReference spatialReference)
    {
        if (string.IsNullOrWhiteSpace(spatialReference.Authority) ||
            string.IsNullOrWhiteSpace(spatialReference.Code))
        {
            throw new NotSupportedException("WMS baseline requires an explicit authority/code CRS identifier such as EPSG:3857.");
        }

        return $"{spatialReference.Authority}:{spatialReference.Code}";
    }

    private static string FormatBbox(Envelope2D bounds, SpatialReference spatialReference)
    {
        var latitudeFirst = string.Equals(spatialReference.Authority, "EPSG", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(spatialReference.Code, "4326", StringComparison.Ordinal);
        return latitudeFirst
            ? string.Create(CultureInfo.InvariantCulture, $"{bounds.MinY},{bounds.MinX},{bounds.MaxY},{bounds.MaxX}")
            : string.Create(CultureInfo.InvariantCulture, $"{bounds.MinX},{bounds.MinY},{bounds.MaxX},{bounds.MaxY}");
    }

    private static string GetMimeType(TileContentType contentType) => contentType switch
    {
        TileContentType.Png => "image/png",
        TileContentType.Jpeg => "image/jpeg",
        TileContentType.WebP => "image/webp",
        _ => throw new ArgumentOutOfRangeException(nameof(contentType)),
    };
}
