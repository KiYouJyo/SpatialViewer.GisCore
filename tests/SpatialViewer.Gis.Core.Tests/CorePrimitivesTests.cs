using SpatialViewer.Gis.Core;
using Xunit;

namespace SpatialViewer.Gis.Core.Tests;

public sealed class CorePrimitivesTests
{
    [Fact]
    public void EnvelopeContainsCoordinateInsideBounds()
    {
        var envelope = new Envelope2D(0, 0, 10, 20);

        Assert.True(envelope.Contains(new GisCoordinate(5, 6)));
        Assert.False(envelope.Contains(new GisCoordinate(11, 6)));
    }

    [Fact]
    public void UnknownSpatialReferenceIsExplicit()
    {
        Assert.True(SpatialReference.Unknown.IsUnknown);
        Assert.False(SpatialReference.FromEpsg(4326).IsUnknown);
    }

    [Fact]
    public void PackedRTreeReturnsOnlyIntersectingEntries()
    {
        var entries = new[]
        {
            new SpatialIndexEntry<int>(new Envelope2D(0, 0, 2, 2), 1),
            new SpatialIndexEntry<int>(new Envelope2D(5, 5, 8, 8), 2),
            new SpatialIndexEntry<int>(new Envelope2D(10, 10, 20, 20), 3),
            new SpatialIndexEntry<int>(new Envelope2D(-5, -5, -1, -1), 4),
        };
        var index = PackedRTree<int>.Build(entries, nodeCapacity: 4);

        var matches = index.Query(new Envelope2D(1, 1, 6, 6));

        Assert.Equal(2, matches.Count);
        Assert.Contains(1, matches);
        Assert.Contains(2, matches);
        Assert.DoesNotContain(3, matches);
        Assert.DoesNotContain(4, matches);
    }

    [Fact]
    public void PackedRTreeHandlesEmptyIndexWithoutInventingMatches()
    {
        var index = PackedRTree<int>.Build(Array.Empty<SpatialIndexEntry<int>>());

        var matches = index.Query(new Envelope2D(0, 0, 1, 1));

        Assert.Equal(0, index.Count);
        Assert.Empty(matches);
    }
}
