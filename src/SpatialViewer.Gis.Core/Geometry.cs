namespace SpatialViewer.Gis.Core;

public readonly record struct GisCoordinate(double X, double Y, double? Z = null, double? M = null);

public readonly record struct Envelope2D(double MinX, double MinY, double MaxX, double MaxY)
{
    public bool IsValid => MinX <= MaxX && MinY <= MaxY;

    public bool Contains(GisCoordinate coordinate) =>
        IsValid &&
        coordinate.X >= MinX && coordinate.X <= MaxX &&
        coordinate.Y >= MinY && coordinate.Y <= MaxY;

    public bool Intersects(Envelope2D other) =>
        IsValid &&
        other.IsValid &&
        MinX <= other.MaxX &&
        MaxX >= other.MinX &&
        MinY <= other.MaxY &&
        MaxY >= other.MinY;

    public static Envelope2D Union(Envelope2D left, Envelope2D right)
    {
        if (!left.IsValid)
        {
            return right;
        }

        if (!right.IsValid)
        {
            return left;
        }

        return new Envelope2D(
            Math.Min(left.MinX, right.MinX),
            Math.Min(left.MinY, right.MinY),
            Math.Max(left.MaxX, right.MaxX),
            Math.Max(left.MaxY, right.MaxY));
    }
}

public readonly record struct GisBoundingBox(Envelope2D XY, double? MinZ = null, double? MaxZ = null);

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

    Envelope2D? Bounds { get; }

    GisBoundingBox? DeclaredBounds { get; }
}

public sealed record PointGeometry(
    GisCoordinate Coordinate,
    GisBoundingBox? DeclaredBounds = null) : IGisGeometry
{
    public GisGeometryType GeometryType => GisGeometryType.Point;

    public Envelope2D? Bounds => new Envelope2D(Coordinate.X, Coordinate.Y, Coordinate.X, Coordinate.Y);
}

public sealed record MultiPointGeometry(
    IReadOnlyList<GisCoordinate> Coordinates,
    GisBoundingBox? DeclaredBounds = null) : IGisGeometry
{
    public GisGeometryType GeometryType => GisGeometryType.MultiPoint;

    public Envelope2D? Bounds => GeometryBounds.FromCoordinates(Coordinates);
}

public sealed record LineStringGeometry(
    IReadOnlyList<GisCoordinate> Coordinates,
    GisBoundingBox? DeclaredBounds = null) : IGisGeometry
{
    public GisGeometryType GeometryType => GisGeometryType.LineString;

    public Envelope2D? Bounds => GeometryBounds.FromCoordinates(Coordinates);
}

public sealed record MultiLineStringGeometry(
    IReadOnlyList<IReadOnlyList<GisCoordinate>> Lines,
    GisBoundingBox? DeclaredBounds = null) : IGisGeometry
{
    public GisGeometryType GeometryType => GisGeometryType.MultiLineString;

    public Envelope2D? Bounds => GeometryBounds.FromCoordinateParts(Lines);
}

public sealed record PolygonGeometry(
    IReadOnlyList<IReadOnlyList<GisCoordinate>> Rings,
    GisBoundingBox? DeclaredBounds = null) : IGisGeometry
{
    public GisGeometryType GeometryType => GisGeometryType.Polygon;

    public Envelope2D? Bounds => GeometryBounds.FromCoordinateParts(Rings);
}

public sealed record MultiPolygonGeometry(
    IReadOnlyList<IReadOnlyList<IReadOnlyList<GisCoordinate>>> Polygons,
    GisBoundingBox? DeclaredBounds = null) : IGisGeometry
{
    public GisGeometryType GeometryType => GisGeometryType.MultiPolygon;

    public Envelope2D? Bounds
    {
        get
        {
            Envelope2D? bounds = null;

            foreach (var polygon in Polygons)
            {
                bounds = GeometryBounds.Union(bounds, GeometryBounds.FromCoordinateParts(polygon));
            }

            return bounds;
        }
    }
}

public sealed record GeometryCollectionGeometry(
    IReadOnlyList<IGisGeometry> Geometries,
    GisBoundingBox? DeclaredBounds = null) : IGisGeometry
{
    public GisGeometryType GeometryType => GisGeometryType.GeometryCollection;

    public Envelope2D? Bounds
    {
        get
        {
            Envelope2D? bounds = null;

            foreach (var geometry in Geometries)
            {
                bounds = GeometryBounds.Union(bounds, geometry.Bounds);
            }

            return bounds;
        }
    }
}

internal static class GeometryBounds
{
    public static Envelope2D? FromCoordinates(IReadOnlyList<GisCoordinate> coordinates)
    {
        if (coordinates.Count == 0)
        {
            return null;
        }

        var first = coordinates[0];
        var minX = first.X;
        var minY = first.Y;
        var maxX = first.X;
        var maxY = first.Y;

        for (var index = 1; index < coordinates.Count; index++)
        {
            var coordinate = coordinates[index];
            minX = Math.Min(minX, coordinate.X);
            minY = Math.Min(minY, coordinate.Y);
            maxX = Math.Max(maxX, coordinate.X);
            maxY = Math.Max(maxY, coordinate.Y);
        }

        return new Envelope2D(minX, minY, maxX, maxY);
    }

    public static Envelope2D? FromCoordinateParts(IReadOnlyList<IReadOnlyList<GisCoordinate>> parts)
    {
        Envelope2D? bounds = null;

        foreach (var part in parts)
        {
            bounds = Union(bounds, FromCoordinates(part));
        }

        return bounds;
    }

    public static Envelope2D? Union(Envelope2D? left, Envelope2D? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return Envelope2D.Union(left.Value, right.Value);
    }
}
