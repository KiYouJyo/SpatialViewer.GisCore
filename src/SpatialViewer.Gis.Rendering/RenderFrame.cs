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

public sealed record GisPointRenderData(GisCoordinate Coordinate);

public sealed record GisPolylineRenderData(IReadOnlyList<GisCoordinate> Coordinates);

public sealed record GisPolygonRenderData(IReadOnlyList<IReadOnlyList<GisCoordinate>> Rings);

public sealed record GisRenderPrimitive(
    GisRenderPrimitiveKind Kind,
    Envelope2D Bounds,
    object Payload,
    string? FeatureId = null,
    IReadOnlyDictionary<string, object?>? Attributes = null);

public sealed record GisRenderFrame(
    Envelope2D ViewExtent,
    IReadOnlyList<GisRenderPrimitive> Primitives);
