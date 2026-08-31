using SpatialViewer.Gis.Core;
using Xunit;

namespace SpatialViewer.Gis.Core.Tests;

public sealed class GeometryTests
{
    [Fact]
    public void EnvelopeIntersectionIncludesTouchingEdges()
    {
        var left = new Envelope2D(0, 0, 10, 10);

        Assert.True(left.Intersects(new Envelope2D(10, 4, 12, 6)));
        Assert.False(left.Intersects(new Envelope2D(10.1, 4, 12, 6)));
    }

    [Fact]
    public void MultiPolygonBoundsCoverEveryPolygon()
    {
        var geometry = new MultiPolygonGeometry(
            new IReadOnlyList<IReadOnlyList<GisCoordinate>>[]
            {
                new IReadOnlyList<GisCoordinate>[]
                {
                    new[]
                    {
                        new GisCoordinate(-4, -3),
                        new GisCoordinate(0, -3),
                        new GisCoordinate(0, 0),
                        new GisCoordinate(-4, -3),
                    },
                },
                new IReadOnlyList<GisCoordinate>[]
                {
                    new[]
                    {
                        new GisCoordinate(10, 10),
                        new GisCoordinate(12, 10),
                        new GisCoordinate(12, 15),
                        new GisCoordinate(10, 10),
                    },
                },
            });

        Assert.Equal(new Envelope2D(-4, -3, 12, 15), geometry.Bounds);
    }

    [Fact]
    public void EmptyGeometryHasNoInventedBounds()
    {
        var geometry = new LineStringGeometry(Array.Empty<GisCoordinate>());

        Assert.Null(geometry.Bounds);
    }
}
