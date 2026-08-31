using System.Net;
using System.Net.Http.Headers;
using SpatialViewer.Formats.Gis.XyzTiles;
using SpatialViewer.Gis.Core;
using Xunit;

namespace SpatialViewer.Gis.Formats.Tests;

public sealed class XyzTileReaderTests
{
    [Fact]
    public async Task BuildsXyzUrlAndCachesSuccessfulResponse()
    {
        var handler = new StubHttpMessageHandler((request, _, _) =>
        {
            Assert.Equal("https://tiles.example/2/1/3.png", request.RequestUri?.AbsoluteUri);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            response.Headers.ETag = new EntityTagHeaderValue("\"abc\"");
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        using var reader = new XyzTileDataSourceReader(
            httpClient,
            TileScheme.Xyz,
            maximumRetries: 0,
            retryDelay: TimeSpan.Zero);

        var first = await reader.ReadTileAsync(
            "https://tiles.example/{z}/{x}/{y}.png",
            "tiles",
            new TileCoordinate(2, 1, 3));
        var second = await reader.ReadTileAsync(
            "https://tiles.example/{z}/{x}/{y}.png",
            "tiles",
            new TileCoordinate(2, 1, 3));

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(TileContentType.Png, first.ContentType);
        Assert.Equal("\"abc\"", first.EntityTag);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task TmsSchemeFlipsCanonicalXyzYAtAdapterBoundary()
    {
        var handler = new StubHttpMessageHandler((request, _, _) =>
        {
            Assert.Equal("https://tiles.example/2/1/0.png", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
            });
        });
        using var httpClient = new HttpClient(handler);
        using var reader = new XyzTileDataSourceReader(
            httpClient,
            TileScheme.Tms,
            maximumRetries: 0,
            retryDelay: TimeSpan.Zero);

        var tile = await reader.ReadTileAsync(
            "https://tiles.example/{z}/{x}/{y}.png",
            "tiles",
            new TileCoordinate(2, 1, 3));

        Assert.NotNull(tile);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task RetriesTransientServerFailureThenSucceeds()
    {
        var handler = new StubHttpMessageHandler((_, requestNumber, _) =>
        {
            if (requestNumber == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF, 0x00 }),
            });
        });
        using var httpClient = new HttpClient(handler);
        using var reader = new XyzTileDataSourceReader(
            httpClient,
            maximumRetries: 1,
            retryDelay: TimeSpan.Zero);

        var tile = await reader.ReadTileAsync(
            "https://tiles.example/{z}/{x}/{y}.jpg",
            "tiles",
            new TileCoordinate(0, 0, 0));

        Assert.NotNull(tile);
        Assert.Equal(TileContentType.Jpeg, tile.ContentType);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task NotFoundReturnsNullWithoutRetrying()
    {
        var handler = new StubHttpMessageHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var httpClient = new HttpClient(handler);
        using var reader = new XyzTileDataSourceReader(
            httpClient,
            maximumRetries: 2,
            retryDelay: TimeSpan.Zero);

        var tile = await reader.ReadTileAsync(
            "https://tiles.example/{z}/{x}/{y}.png",
            "tiles",
            new TileCoordinate(0, 0, 0));

        Assert.Null(tile);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CallerCancellationIsNotRewrittenAsTimeout()
    {
        var handler = new StubHttpMessageHandler(async (_, _, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        using var reader = new XyzTileDataSourceReader(
            httpClient,
            requestTimeout: TimeSpan.FromSeconds(5),
            maximumRetries: 0,
            retryDelay: TimeSpan.Zero);
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await reader.ReadTileAsync(
                "https://tiles.example/{z}/{x}/{y}.png",
                "tiles",
                new TileCoordinate(0, 0, 0),
                cancellationSource.Token));
    }

    [Fact]
    public async Task AdapterTimeoutProducesExplicitTimeoutException()
    {
        var handler = new StubHttpMessageHandler(async (_, _, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        using var reader = new XyzTileDataSourceReader(
            httpClient,
            requestTimeout: TimeSpan.FromMilliseconds(25),
            maximumRetries: 0,
            retryDelay: TimeSpan.Zero);

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await reader.ReadTileAsync(
                "https://tiles.example/{z}/{x}/{y}.png",
                "tiles",
                new TileCoordinate(0, 0, 0)));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> _handler;
        private int _requestCount;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestNumber = Interlocked.Increment(ref _requestCount);
            return _handler(request, requestNumber, cancellationToken);
        }
    }
}
