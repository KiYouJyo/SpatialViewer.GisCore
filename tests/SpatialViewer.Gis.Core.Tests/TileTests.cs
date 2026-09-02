using SpatialViewer.Gis.Core;
using Xunit;

namespace SpatialViewer.Gis.Core.Tests;

public sealed class TileTests
{
    [Fact]
    public void XyzAndTmsRowsRoundTrip()
    {
        var coordinate = new TileCoordinate(3, 5, 2);

        var tmsRow = coordinate.ToTmsRow();
        var restored = TileCoordinate.FromTmsRow(3, 5, tmsRow);

        Assert.Equal(5, tmsRow);
        Assert.Equal(coordinate, restored);
    }

    [Fact]
    public void WebMercatorBoundsFollowNorthOriginXyzSemantics()
    {
        var bounds = WebMercatorTileMath.GetBounds(new TileCoordinate(1, 1, 1));

        Assert.Equal(0d, bounds.MinX, 8);
        Assert.Equal(-WebMercatorTileMath.MaximumCoordinate, bounds.MinY, 8);
        Assert.Equal(WebMercatorTileMath.MaximumCoordinate, bounds.MaxX, 8);
        Assert.Equal(0d, bounds.MaxY, 8);
    }

    [Fact]
    public void TileCacheEvictsLeastRecentlyUsedEntryByByteBudget()
    {
        var cache = new TileMemoryCache(8);
        var firstKey = new TileCacheKey("source", "tiles", new TileCoordinate(1, 0, 0));
        var secondKey = new TileCacheKey("source", "tiles", new TileCoordinate(1, 1, 0));
        var thirdKey = new TileCacheKey("source", "tiles", new TileCoordinate(1, 0, 1));
        cache.Set(firstKey, CreateTile(firstKey.Coordinate, 1));
        cache.Set(secondKey, CreateTile(secondKey.Coordinate, 2));
        Assert.True(cache.TryGet(firstKey, out _));

        cache.Set(thirdKey, CreateTile(thirdKey.Coordinate, 3));

        Assert.True(cache.TryGet(firstKey, out _));
        Assert.False(cache.TryGet(secondKey, out _));
        Assert.True(cache.TryGet(thirdKey, out _));
    }

    [Fact]
    public async Task TileRequestCoordinatorCancelsSupersededRequest()
    {
        using var coordinator = new TileRequestCoordinator();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = coordinator.RunLatestAsync(async token =>
        {
            firstStarted.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return 1;
        }).AsTask();
        await firstStarted.Task;

        var second = coordinator.RunLatestAsync<int>(_ => ValueTask.FromResult(2)).AsTask();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first);
        Assert.Equal(2, await second);
    }

    private static TileReadResult CreateTile(TileCoordinate coordinate, byte value) => new(
        coordinate,
        TileContentType.Png,
        new byte[] { value, value, value, value });
}
