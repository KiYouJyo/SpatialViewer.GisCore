namespace SpatialViewer.Gis.Core;

public readonly record struct GisCoordinate(double X, double Y, double? Z = null);

public readonly record struct Envelope2D(double MinX, double MinY, double MaxX, double MaxY)
{
    public bool IsValid => MinX <= MaxX && MinY <= MaxY;

    public bool Contains(GisCoordinate coordinate) =>
        IsValid &&
        coordinate.X >= MinX && coordinate.X <= MaxX &&
        coordinate.Y >= MinY && coordinate.Y <= MaxY;
}

public enum GisGeometryType
{
    Point,
    MultiPoint,
    LineString,
    MultiLineString,
    Polygon,
    MultiPolygon,
    GeometryCollection,
}

public interface IGisGeometry
{
    GisGeometryType GeometryType { get; }

    Envelope2D Bounds { get; }
}

public sealed record PointGeometry(GisCoordinate Coordinate) : IGisGeometry
{
    public GisGeometryType GeometryType => GisGeometryType.Point;

    public Envelope2D Bounds => new(Coordinate.X, Coordinate.Y, Coordinate.X, Coordinate.Y);
}
