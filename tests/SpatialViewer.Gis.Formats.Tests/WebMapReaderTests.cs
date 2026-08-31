using System.Net;
using System.Net.Http.Headers;
using SpatialViewer.Formats.Gis.WebMap;
using SpatialViewer.Gis.Core;
using Xunit;

namespace SpatialViewer.Gis.Formats.Tests;

public sealed class WebMapReaderTests
{
    [Fact]
    public async Task WmsBuildsVersion130GetMapForWebMercator()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var query = ParseQuery(request.RequestUri);
            Assert.Equal("WMS", query["SERVICE"]);
            Assert.Equal("GetMap", query["REQUEST"]);
            Assert.Equal("1.3.0", query["VERSION"]);
            Assert.Equal("roads", query["LAYERS"]);
            Assert.Equal("EPSG:3857", query["CRS"]);
            Assert.Equal("1,2,3,4", query["BBOX"]);
            Assert.Equal("512", query["WIDTH"]);
            Assert.Equal("256", query["HEIGHT"]);
            Assert.Equal("image/png", query["FORMAT"]);
            Assert.Equal("TRUE", query["TRANSPARENT"]);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        using var reader = new WmsMapDataSourceReader(httpClient, maximumRetries: 0, retryDelay: TimeSpan.Zero);
        var request = new MapImageRequest(
            new Envelope2D(1, 2, 3, 4),
            512,
            256,
            SpatialReference.FromEpsg(3857));

        var result = await reader.ReadMapAsync("https://maps.example/wms", "roads", request);

        Assert.Equal(TileContentType.Png, result.ContentType);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task WmsVersion130UsesLatitudeFirstAxisForEpsg4326()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var query = ParseQuery(request.RequestUri);
            Assert.Equal("EPSG:4326", query["CRS"]);
            Assert.Equal("20,10,40,30", query["BBOX"]);
            return Task.FromResult(CreatePngResponse());
        });
        using var httpClient = new HttpClient(handler);
        using var reader = new WmsMapDataSourceReader(httpClient, maximumRetries: 0, retryDelay: TimeSpan.Zero);
        var request = new MapImageRequest(
            new Envelope2D(10, 20, 30, 40),
            256,
            256,
            SpatialReference.FromEpsg(4326));

        await reader.ReadMapAsync("https://maps.example/wms?token=abc", "layer", request);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task WmtsBuildsKvpRequestAndCachesTile()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var query = ParseQuery(request.RequestUri);
            Assert.Equal("abc", query["token"]);
            Assert.Equal("WMTS", query["SERVICE"]);
            Assert.Equal("GetTile", query["REQUEST"]);
            Assert.Equal("1.0.0", query["VERSION"]);
            Assert.Equal("base", query["LAYER"]);
            Assert.Equal("GoogleMapsCompatible", query["TILEMATRIXSET"]);
            Assert.Equal("EPSG:3857:2", query["TILEMATRIX"]);
            Assert.Equal("3", query["TILEROW"]);
            Assert.Equal("1", query["TILECOL"]);
            return Task.FromResult(CreatePngResponse());
        });
        using var httpClient = new HttpClient(handler);
        using var reader = new WmtsTileDataSourceReader(
            httpClient,
            "GoogleMapsCompatible",
            "EPSG:3857:{z}",
            maximumRetries: 0,
            retryDelay: TimeSpan.Zero);
        var coordinate = new TileCoordinate(2, 1, 3);

        var first = await reader.ReadTileAsync("https://maps.example/wmts?token=abc", "base", coordinate);
        var second = await reader.ReadTileAsync("https://maps.example/wmts?token=abc", "base", coordinate);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task WmtsCanFlipCanonicalXyzRowForTmsMatrix()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var query = ParseQuery(request.RequestUri);
            Assert.Equal("0", query["TILEROW"]);
            return Task.FromResult(CreatePngResponse());
        });
        using var httpClient = new HttpClient(handler);
        using var reader = new WmtsTileDataSourceReader(
            httpClient,
            "custom",
            rowScheme: TileScheme.Tms,
            maximumRetries: 0,
            retryDelay: TimeSpan.Zero);

        var tile = await reader.ReadTileAsync(
            "https://maps.example/wmts",
            "base",
            new TileCoordinate(2, 1, 3));

        Assert.NotNull(tile);
    }

    private static HttpResponseMessage CreatePngResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return response;
    }

    private static Dictionary<string, string> ParseQuery(Uri? uri)
    {
        Assert.NotNull(uri);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0]);
            var value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, Task<HttpResponseMessage>> _handler;
        private int _requestCount;

        public StubHttpMessageHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _handler(request, Interlocked.Increment(ref _requestCount));
        }
    }
}
