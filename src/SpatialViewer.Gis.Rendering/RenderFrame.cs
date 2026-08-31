using SpatialViewer.Gis.Core;

namespace SpatialViewer.Gis.Rendering;

public enum GisRenderPrimitiveKind
{
    Point,
    Polyline,
    Polygon,
    RasterTile,
    Label,
}

public sealed record GisRenderPrimitive(
    GisRenderPrimitiveKind Kind,
    Envelope2D Bounds,
    object Payload);

public sealed record GisRenderFrame(
    Envelope2D ViewExtent,
    IReadOnlyList<GisRenderPrimitive> Primitives);
