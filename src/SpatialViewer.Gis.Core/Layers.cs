namespace SpatialViewer.Gis.Core;

public enum GisLayerKind
{
    Vector,
    Raster,
    Tile,
}

public sealed record GisFeature(
    string? Id,
    IGisGeometry? Geometry,
    IReadOnlyDictionary<string, object?> Attributes,
    GisBoundingBox? DeclaredBounds = null)
{
    public Envelope2D? Bounds => Geometry?.Bounds ?? DeclaredBounds?.XY;
}

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
    : GisLayerMetadata(Name, GisLayerKind.Raster, SpatialReference, Bounds)
{
    public RasterGeoTransform? GeoTransform { get; init; }

    public RasterPixelAnchor PixelAnchor { get; init; } = RasterPixelAnchor.Area;

    public IReadOnlyList<RasterBandMetadata> Bands { get; init; } = Array.Empty<RasterBandMetadata>();

    public IReadOnlyList<RasterOverviewMetadata> Overviews { get; init; } = Array.Empty<RasterOverviewMetadata>();

    public string? ColorModel { get; init; }

    public bool IsTiled { get; init; }
}
