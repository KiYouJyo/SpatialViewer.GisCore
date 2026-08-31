using SpatialViewer.Formats.Gis;
using SpatialViewer.Gis.Core;
using Xunit;

namespace SpatialViewer.Gis.Formats.Tests;

public sealed class RasterViewportReaderTests
{
    [Fact]
    public async Task ReusesCachedViewportRead()
    {
        var source = new TestRasterReader();
        using var viewport = new RasterViewportReader(source, new RasterTileCache(1024));
        var request = new RasterReadRequest(new RasterWindow(0, 0, 1, 1), 1, 1);

        var first = await viewport.ReadLatestAsync("cache-source.tif", "raster", request);
        var second = await viewport.ReadLatestAsync("cache-source.tif", "raster", request);

        Assert.Same(first, second);
        Assert.Equal(1, source.ReadCount);
    }

    [Fact]
    public async Task NewViewportCancelsSupersededAdapterRead()
    {
        var source = new TestRasterReader(delayFirstRead: true);
        using var viewport = new RasterViewportReader(source, new RasterTileCache(1024));
        var firstRequest = new RasterReadRequest(new RasterWindow(0, 0, 1, 1), 1, 1);
        var secondRequest = new RasterReadRequest(new RasterWindow(1, 0, 1, 1), 1, 1);

        var first = viewport.ReadLatestAsync("cancel-source.tif", "raster", firstRequest).AsTask();
        await source.FirstReadStarted.Task;
        var second = viewport.ReadLatestAsync("cancel-source.tif", "raster", secondRequest).AsTask();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first);
        Assert.Equal(2, (await second).Pixels.Span[0]);
    }

    private sealed class TestRasterReader : IRasterDataSourceReader
    {
        private readonly bool _delayFirstRead;
        private int _readCount;

        public TestRasterReader(bool delayFirstRead = false)
        {
            _delayFirstRead = delayFirstRead;
        }

        public string FormatId => "test";

        public int ReadCount => _readCount;

        public TaskCompletionSource FirstReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<GisDatasetMetadata> ReadMetadataAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                new GisDatasetMetadata(
                    path,
                    FormatId,
                    new GisLayerMetadata[]
                    {
                        new RasterLayerMetadata("raster", SpatialReference.Unknown, null, 2, 1, 4),
                    }));

        public async ValueTask<RasterReadResult> ReadRasterAsync(
            string path,
            string layerName,
            RasterReadRequest request,
            CancellationToken cancellationToken = default)
        {
            var readNumber = Interlocked.Increment(ref _readCount);
            if (_delayFirstRead && readNumber == 1)
            {
                FirstReadStarted.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var value = checked((byte)readNumber);
            return new RasterReadResult(
                1,
                1,
                RasterPixelFormat.Rgba32,
                new byte[] { value, value, value, 255 },
                0,
                request.Window);
        }
    }
}
