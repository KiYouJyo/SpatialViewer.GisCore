using SpatialViewer.Gis.Core;

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
}
