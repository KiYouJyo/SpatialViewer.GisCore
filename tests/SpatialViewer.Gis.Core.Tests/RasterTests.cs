using SpatialViewer.Gis.Core;
using Xunit;

namespace SpatialViewer.Gis.Core.Tests;

public sealed class RasterTests
{
    [Fact]
    public void GeoTransformRoundTripsRotatedCoordinates()
    {
        var transform = new RasterGeoTransform(100, 2, 0.25, 200, -0.5, -3);
        var world = transform.PixelToWorld(12.5, 8.25);

        var success = transform.TryWorldToPixel(world.X, world.Y, out var column, out var row);

        Assert.True(success);
        Assert.Equal(12.5, column, 10);
        Assert.Equal(8.25, row, 10);
    }

    [Fact]
    public void GeoTransformBoundsUseAllFourCorners()
    {
        var transform = new RasterGeoTransform(0, 2, 1, 10, 0.5, -2);

        var bounds = transform.GetBounds(10, 5);

        Assert.Equal(new Envelope2D(0, 0, 25, 15), bounds);
    }

    [Fact]
    public void OverviewSelectorChoosesClosestNonOversampledLevel()
    {
        var overviews = new[]
        {
            new RasterOverviewMetadata(1, 500, 500, 2, 2),
            new RasterOverviewMetadata(2, 250, 250, 4, 4),
            new RasterOverviewMetadata(3, 125, 125, 8, 8),
        };

        var level = RasterOverviewSelector.SelectLevel(
            new RasterWindow(0, 0, 1000, 1000),
            220,
            220,
            overviews);

        Assert.Equal(2, level);
    }

    [Fact]
    public void TileCacheEvictsLeastRecentlyUsedEntryByByteBudget()
    {
        var cache = new RasterTileCache(8);
        var firstKey = new RasterTileCacheKey("source", "layer", 0, new RasterWindow(0, 0, 1, 1), 1, 1);
        var secondKey = new RasterTileCacheKey("source", "layer", 0, new RasterWindow(1, 0, 1, 1), 1, 1);
        var thirdKey = new RasterTileCacheKey("source", "layer", 0, new RasterWindow(2, 0, 1, 1), 1, 1);
        cache.Set(firstKey, CreatePixel(1));
        cache.Set(secondKey, CreatePixel(2));
        Assert.True(cache.TryGet(firstKey, out _));

        cache.Set(thirdKey, CreatePixel(3));

        Assert.True(cache.TryGet(firstKey, out _));
        Assert.False(cache.TryGet(secondKey, out _));
        Assert.True(cache.TryGet(thirdKey, out _));
    }

    [Fact]
    public async Task RequestCoordinatorCancelsSupersededRequest()
    {
        using var coordinator = new RasterRequestCoordinator();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = coordinator.RunLatestAsync(async token =>
        {
            firstStarted.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            return 1;
        }).AsTask();
        await firstStarted.Task;

        var second = coordinator.RunLatestAsync<int>(_ => ValueTask.FromResult(2)).AsTask();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first);
        Assert.Equal(2, await second);
    }

    private static RasterReadResult CreatePixel(byte value) => new(
        1,
        1,
        RasterPixelFormat.Rgba32,
        new byte[] { value, value, value, 255 },
        0,
        new RasterWindow(0, 0, 1, 1));
}
