using System.Net;
using System.Net.Http.Headers;
using SpatialViewer.Formats.Gis.RemoteCog;
using SpatialViewer.Gis.Core;
using Xunit;

namespace SpatialViewer.Gis.Formats.Tests;

public sealed class RemoteCogReaderTests
{
    [Fact]
    public async Task ReadsMetadataUsingOnlyBoundedRangeResponses()
    {
        var source = File.ReadAllBytes(GetFixturePath());
        var handler = new RangeHttpMessageHandler(source, requireRange: true);
        using var httpClient = new HttpClient(handler);
        using var reader = new RemoteCogDataSourceReader(
            httpClient,
            rangeBlockSize: 128,
            maximumCachedBlocks: 8);

        var metadata = await reader.ReadMetadataAsync("https://example.test/raster.tif");
        var layer = Assert.IsType<RasterLayerMetadata>(Assert.Single(metadata.Layers));

        Assert.Equal("remote-cog", metadata.SourceKind);
        Assert.Equal(32, layer.Width);
        Assert.Equal(32, layer.Height);
        Assert.True(layer.IsTiled);
        Assert.Equal(SpatialReference.FromEpsg(3857), layer.SpatialReference);
        Assert.Equal(new RasterGeoTransform(100, 10, 0, 200, 0, -10), layer.GeoTransform);
        Assert.Single(layer.Overviews);
        Assert.True(handler.RequestCount > 1);
        Assert.Equal(128, handler.MaximumResponseBytes);
        Assert.All(handler.Requests, request => Assert.NotNull(request.Range));
    }

    [Fact]
    public async Task ReadsRequestedWindowWithoutDownloadingWholeFile()
    {
        var source = File.ReadAllBytes(GetFixturePath());
        var handler = new RangeHttpMessageHandler(source, requireRange: true);
        using var httpClient = new HttpClient(handler);
        using var reader = new RemoteCogDataSourceReader(
            httpClient,
            rangeBlockSize: 128,
            maximumCachedBlocks: 8);

        var result = await reader.ReadRasterAsync(
            "https://example.test/raster.tif",
            "raster",
            new RasterReadRequest(new RasterWindow(4, 6, 8, 10), 8, 10));

        Assert.Equal(0, result.OverviewLevel);
        AssertPixel(result, 0, 0, 4, 6, 10, 255);
        AssertPixel(result, 7, 9, 11, 15, 26, 255);
        Assert.True(handler.TotalResponseBytes < source.Length * 2L);
        Assert.Equal(128, handler.MaximumResponseBytes);
        Assert.All(handler.Requests, request => Assert.NotNull(request.Range));
    }

    [Fact]
    public async Task SelectsInternalOverviewOverHttpRange()
    {
        var source = File.ReadAllBytes(GetFixturePath());
        var handler = new RangeHttpMessageHandler(source, requireRange: true);
        using var httpClient = new HttpClient(handler);
        using var reader = new RemoteCogDataSourceReader(
            httpClient,
            rangeBlockSize: 128,
            maximumCachedBlocks: 8);

        var result = await reader.ReadRasterAsync(
            "https://example.test/raster.tif",
            "raster",
            new RasterReadRequest(new RasterWindow(0, 0, 32, 32), 8, 8));

        Assert.Equal(1, result.OverviewLevel);
        AssertPixel(result, 0, 0, 2, 2, 4, 255);
        Assert.All(handler.Requests, request => Assert.NotNull(request.Range));
    }

    [Fact]
    public async Task RejectsServerThatIgnoresRangeRequests()
    {
        var source = File.ReadAllBytes(GetFixturePath());
        var handler = new RangeHttpMessageHandler(source, requireRange: false);
        using var httpClient = new HttpClient(handler);
        using var reader = new RemoteCogDataSourceReader(httpClient, rangeBlockSize: 128);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            async () => await reader.ReadMetadataAsync("https://example.test/raster.tif"));

        Assert.Contains("HTTP Range", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);
    }

    private static string GetFixturePath() => Path.Combine(
        AppContext.BaseDirectory,
        "fixtures",
        "geotiff",
        "phase3-tiled-overview.tif");

    private static void AssertPixel(
        RasterReadResult result,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        var pixels = result.Pixels.Span;
        var offset = ((y * result.Width) + x) * 4;
        Assert.Equal(red, pixels[offset]);
        Assert.Equal(green, pixels[offset + 1]);
        Assert.Equal(blue, pixels[offset + 2]);
        Assert.Equal(alpha, pixels[offset + 3]);
    }

    private sealed class RangeHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] _source;
        private readonly bool _requireRange;
        private readonly List<CapturedRequest> _requests = new();
        private int _requestCount;
        private long _totalResponseBytes;
        private int _maximumResponseBytes;

        public RangeHttpMessageHandler(byte[] source, bool requireRange)
        {
            _source = source;
            _requireRange = requireRange;
        }

        public int RequestCount => _requestCount;

        public long TotalResponseBytes => _totalResponseBytes;

        public int MaximumResponseBytes => _maximumResponseBytes;

        public IReadOnlyList<CapturedRequest> Requests => _requests;

        protected override HttpResponseMessage Send(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            CreateResponse(request, cancellationToken);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(CreateResponse(request, cancellationToken));

        private HttpResponseMessage CreateResponse(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requestCount);
            var range = request.Headers.Range?.Ranges.SingleOrDefault();
            lock (_requests)
            {
                _requests.Add(new CapturedRequest(request.RequestUri, range));
            }

            if (!_requireRange)
            {
                RecordResponseBytes(_source.Length);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_source),
                };
            }

            if (range?.From is null)
            {
                throw new InvalidOperationException("Remote COG reader issued an HTTP request without a byte range.");
            }

            var start = range.From.Value;
            if (start >= _source.LongLength)
            {
                return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
            }

            var requestedEnd = range.To ?? (_source.LongLength - 1);
            var end = Math.Min(requestedEnd, _source.LongLength - 1);
            var length = checked((int)(end - start + 1));
            var content = new byte[length];
            Buffer.BlockCopy(_source, checked((int)start), content, 0, length);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(content),
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, _source.LongLength);
            RecordResponseBytes(length);
            return response;
        }

        private void RecordResponseBytes(int length)
        {
            Interlocked.Add(ref _totalResponseBytes, length);
            int current;
            while (length > (current = Volatile.Read(ref _maximumResponseBytes)) &&
                   Interlocked.CompareExchange(ref _maximumResponseBytes, length, current) != current)
            {
            }
        }
    }

    public sealed record CapturedRequest(Uri? Uri, RangeItemHeaderValue? Range);
}
