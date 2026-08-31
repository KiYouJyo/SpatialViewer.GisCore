using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis.Shapefile;

internal static class ShapefileBoundsReader
{
    private const int NullShape = 0;
    private const int Point = 1;
    private const int PolyLine = 3;
    private const int Polygon = 5;
    private const int MultiPoint = 8;
    private const int PointZ = 11;
    private const int PolyLineZ = 13;
    private const int PolygonZ = 15;
    private const int MultiPointZ = 18;
    private const int PointM = 21;
    private const int PolyLineM = 23;
    private const int PolygonM = 25;
    private const int MultiPointM = 28;

    public static Envelope2D? ReadBounds(ReadOnlySpan<byte> contentPrefix)
    {
        EnsureAvailable(contentPrefix, 0, 4, "shape type");
        var shapeType = ShapefileBinary.ReadInt32LittleEndian(contentPrefix, 0);

        return shapeType switch
        {
            NullShape => null,
            Point or PointZ or PointM => ReadPointBounds(contentPrefix),
            PolyLine or PolyLineZ or PolyLineM or
            Polygon or PolygonZ or PolygonM or
            MultiPoint or MultiPointZ or MultiPointM => ReadBoxBounds(contentPrefix),
            _ => throw new NotSupportedException($"Shapefile shape type {shapeType} is not supported."),
        };
    }

    private static Envelope2D ReadPointBounds(ReadOnlySpan<byte> content)
    {
        EnsureAvailable(content, 4, 16, "point XY coordinates");
        var x = ShapefileBinary.ReadDoubleLittleEndian(content, 4);
        var y = ShapefileBinary.ReadDoubleLittleEndian(content, 12);
        return new Envelope2D(x, y, x, y);
    }

    private static Envelope2D ReadBoxBounds(ReadOnlySpan<byte> content)
    {
        EnsureAvailable(content, 4, 32, "shape bounding box");
        return new Envelope2D(
            ShapefileBinary.ReadDoubleLittleEndian(content, 4),
            ShapefileBinary.ReadDoubleLittleEndian(content, 12),
            ShapefileBinary.ReadDoubleLittleEndian(content, 20),
            ShapefileBinary.ReadDoubleLittleEndian(content, 28));
    }

    private static void EnsureAvailable(
        ReadOnlySpan<byte> content,
        int offset,
        int length,
        string description)
    {
        if (offset < 0 || length < 0 || content.Length - offset < length)
        {
            throw new InvalidDataException($"Shapefile record is truncated while reading {description}.");
        }
    }
}
