using SpatialViewer.Gis.Core;
using SpatialViewer.Gis.Rendering;
using Xunit;

namespace SpatialViewer.Gis.Rendering.Tests;

public sealed class RenderFrameTests
{
    [Fact]
    public void RenderFramePreservesViewExtent()
    {
        var extent = new Envelope2D(1, 2, 3, 4);
        var frame = new GisRenderFrame(extent, Array.Empty<GisRenderPrimitive>());

        Assert.Equal(extent, frame.ViewExtent);
        Assert.Empty(frame.Primitives);
    }
}
