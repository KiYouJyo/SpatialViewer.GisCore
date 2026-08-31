namespace SpatialViewer.Gis.Core;

public enum GisLayerKind
{
    Vector,
    Raster,
    Tile,
}

public sealed record GisFeature(
    string? Id,
    IGisGeometry Geometry,
    IReadOnlyDictionary<string, object?> Attributes);

public abstract record GisLayerMetadata(
    string Name,
    GisLayerKind Kind,
    SpatialReference SpatialReference,
    Envelope2D? Bounds);

public sealed record VectorLayerMetadata(
    string Name,
    SpatialReference SpatialReference,
    Envelope2D? Bounds,
    GisGeometryType? GeometryType,
    long? FeatureCount)
    : GisLayerMetadata(Name, GisLayerKind.Vector, SpatialReference, Bounds);

public sealed record RasterLayerMetadata(
    string Name,
    SpatialReference SpatialReference,
    Envelope2D? Bounds,
    int Width,
    int Height,
    int BandCount)
    : GisLayerMetadata(Name, GisLayerKind.Raster, SpatialReference, Bounds);
