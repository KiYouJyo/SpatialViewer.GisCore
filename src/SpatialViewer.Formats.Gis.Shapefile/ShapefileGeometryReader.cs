using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis.Shapefile;

internal static class ShapefileGeometryReader
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

    public static GisGeometryType? MapGeometryType(int shapeType) => shapeType switch
    {
        NullShape => null,
        Point or PointZ or PointM => GisGeometryType.Point,
        PolyLine or PolyLineZ or PolyLineM => GisGeometryType.MultiLineString,
        Polygon or PolygonZ or PolygonM => GisGeometryType.Polygon,
        MultiPoint or MultiPointZ or MultiPointM => GisGeometryType.MultiPoint,
        _ => throw new NotSupportedException($"Shapefile shape type {shapeType} is not supported."),
    };

    public static IGisGeometry? Parse(ReadOnlySpan<byte> content)
    {
        EnsureAvailable(content, 0, 4, "shape type");
        var shapeType = ShapefileBinary.ReadInt32LittleEndian(content, 0);

        return shapeType switch
        {
            NullShape => null,
            Point => ParsePoint(content, hasZ: false, hasM: false),
            PointZ => ParsePoint(content, hasZ: true, hasM: true),
            PointM => ParsePoint(content, hasZ: false, hasM: true),
            PolyLine => ParsePolyLine(content, hasZ: false, hasM: false),
            PolyLineZ => ParsePolyLine(content, hasZ: true, hasM: true),
            PolyLineM => ParsePolyLine(content, hasZ: false, hasM: true),
            Polygon => ParsePolygon(content, hasZ: false, hasM: false),
            PolygonZ => ParsePolygon(content, hasZ: true, hasM: true),
            PolygonM => ParsePolygon(content, hasZ: false, hasM: true),
            MultiPoint => ParseMultiPoint(content, hasZ: false, hasM: false),
            MultiPointZ => ParseMultiPoint(content, hasZ: true, hasM: true),
            MultiPointM => ParseMultiPoint(content, hasZ: false, hasM: true),
            _ => throw new NotSupportedException($"Shapefile shape type {shapeType} is not supported."),
        };
    }

    private static PointGeometry ParsePoint(ReadOnlySpan<byte> content, bool hasZ, bool hasM)
    {
        EnsureAvailable(content, 4, 16, "point XY coordinates");
        var x = ShapefileBinary.ReadDoubleLittleEndian(content, 4);
        var y = ShapefileBinary.ReadDoubleLittleEndian(content, 12);
        double? z = null;
        double? m = null;
        var offset = 20;

        if (hasZ)
        {
            EnsureAvailable(content, offset, 8, "point Z coordinate");
            z = ShapefileBinary.ReadDoubleLittleEndian(content, offset);
            offset += 8;
        }

        if (hasM && content.Length >= offset + 8)
        {
            m = ShapefileBinary.NormalizeMeasure(ShapefileBinary.ReadDoubleLittleEndian(content, offset));
        }

        return new PointGeometry(new GisCoordinate(x, y, z, m));
    }

    private static IGisGeometry ParsePolyLine(ReadOnlySpan<byte> content, bool hasZ, bool hasM)
    {
        var parsed = ParseParts(content, hasZ, hasM, "polyline");
        var declared = CreateDeclaredBounds(content, parsed.MinZ, parsed.MaxZ);

        if (parsed.Parts.Count == 1)
        {
            return new LineStringGeometry(parsed.Parts[0], declared);
        }

        return new MultiLineStringGeometry(parsed.Parts, declared);
    }

    private static PolygonGeometry ParsePolygon(ReadOnlySpan<byte> content, bool hasZ, bool hasM)
    {
        var parsed = ParseParts(content, hasZ, hasM, "polygon");
        return new PolygonGeometry(
            parsed.Parts,
            CreateDeclaredBounds(content, parsed.MinZ, parsed.MaxZ));
    }

    private static MultiPointGeometry ParseMultiPoint(ReadOnlySpan<byte> content, bool hasZ, bool hasM)
    {
        EnsureAvailable(content, 4, 36, "multipoint header");
        var pointCount = ShapefileBinary.ReadInt32LittleEndian(content, 36);
        if (pointCount < 0)
        {
            throw new InvalidDataException("Shapefile MultiPoint point count cannot be negative.");
        }

        var pointsOffset = 40;
        var pointsBytes = checked(pointCount * 16);
        EnsureAvailable(content, pointsOffset, pointsBytes, "multipoint XY array");

        var zOffset = pointsOffset + pointsBytes;
        double? minZ = null;
        double? maxZ = null;
        var zValues = new double?[pointCount];
        var mValues = new double?[pointCount];

        if (hasZ)
        {
            EnsureAvailable(content, zOffset, checked(16 + pointCount * 8), "multipoint Z array");
            minZ = ShapefileBinary.ReadDoubleLittleEndian(content, zOffset);
            maxZ = ShapefileBinary.ReadDoubleLittleEndian(content, zOffset + 8);
            var valuesOffset = zOffset + 16;
            for (var index = 0; index < pointCount; index++)
            {
                zValues[index] = ShapefileBinary.ReadDoubleLittleEndian(content, valuesOffset + (index * 8));
            }

            zOffset = valuesOffset + (pointCount * 8);
        }

        if (hasM && content.Length >= zOffset + 16 + (pointCount * 8))
        {
            var valuesOffset = zOffset + 16;
            for (var index = 0; index < pointCount; index++)
            {
                mValues[index] = ShapefileBinary.NormalizeMeasure(
                    ShapefileBinary.ReadDoubleLittleEndian(content, valuesOffset + (index * 8)));
            }
        }

        var coordinates = new List<GisCoordinate>(pointCount);
        for (var index = 0; index < pointCount; index++)
        {
            coordinates.Add(new GisCoordinate(
                ShapefileBinary.ReadDoubleLittleEndian(content, pointsOffset + (index * 16)),
                ShapefileBinary.ReadDoubleLittleEndian(content, pointsOffset + (index * 16) + 8),
                zValues[index],
                mValues[index]));
        }

        return new MultiPointGeometry(coordinates, CreateDeclaredBounds(content, minZ, maxZ));
    }

    private static ParsedParts ParseParts(
        ReadOnlySpan<byte> content,
        bool hasZ,
        bool hasM,
        string geometryName)
    {
        EnsureAvailable(content, 4, 40, $"{geometryName} header");
        var partCount = ShapefileBinary.ReadInt32LittleEndian(content, 36);
        var pointCount = ShapefileBinary.ReadInt32LittleEndian(content, 40);

        if (partCount < 0 || pointCount < 0)
        {
            throw new InvalidDataException($"Shapefile {geometryName} counts cannot be negative.");
        }

        if (partCount == 0 && pointCount != 0)
        {
            throw new InvalidDataException($"Shapefile {geometryName} contains points but no parts.");
        }

        var partsOffset = 44;
        var pointsOffset = checked(partsOffset + (partCount * 4));
        var pointsBytes = checked(pointCount * 16);
        EnsureAvailable(content, partsOffset, checked(partCount * 4), $"{geometryName} parts array");
        EnsureAvailable(content, pointsOffset, pointsBytes, $"{geometryName} XY array");

        var starts = new int[partCount + 1];
        for (var index = 0; index < partCount; index++)
        {
            starts[index] = ShapefileBinary.ReadInt32LittleEndian(content, partsOffset + (index * 4));
        }

        starts[partCount] = pointCount;
        ValidatePartStarts(starts, pointCount, geometryName);

        var valuesOffset = pointsOffset + pointsBytes;
        double? minZ = null;
        double? maxZ = null;
        var zValues = new double?[pointCount];
        var mValues = new double?[pointCount];

        if (hasZ)
        {
            EnsureAvailable(content, valuesOffset, checked(16 + pointCount * 8), $"{geometryName} Z array");
            minZ = ShapefileBinary.ReadDoubleLittleEndian(content, valuesOffset);
            maxZ = ShapefileBinary.ReadDoubleLittleEndian(content, valuesOffset + 8);
            valuesOffset += 16;

            for (var index = 0; index < pointCount; index++)
            {
                zValues[index] = ShapefileBinary.ReadDoubleLittleEndian(content, valuesOffset + (index * 8));
            }

            valuesOffset += pointCount * 8;
        }

        if (hasM && content.Length >= valuesOffset + 16 + (pointCount * 8))
        {
            valuesOffset += 16;
            for (var index = 0; index < pointCount; index++)
            {
                mValues[index] = ShapefileBinary.NormalizeMeasure(
                    ShapefileBinary.ReadDoubleLittleEndian(content, valuesOffset + (index * 8)));
            }
        }

        var parts = new List<IReadOnlyList<GisCoordinate>>(partCount);
        for (var partIndex = 0; partIndex < partCount; partIndex++)
        {
            var start = starts[partIndex];
            var end = starts[partIndex + 1];
            var coordinates = new List<GisCoordinate>(end - start);

            for (var pointIndex = start; pointIndex < end; pointIndex++)
            {
                coordinates.Add(new GisCoordinate(
                    ShapefileBinary.ReadDoubleLittleEndian(content, pointsOffset + (pointIndex * 16)),
                    ShapefileBinary.ReadDoubleLittleEndian(content, pointsOffset + (pointIndex * 16) + 8),
                    zValues[pointIndex],
                    mValues[pointIndex]));
            }

            parts.Add(coordinates);
        }

        return new ParsedParts(parts, minZ, maxZ);
    }

    private static void ValidatePartStarts(int[] starts, int pointCount, string geometryName)
    {
        if (starts.Length == 1)
        {
            return;
        }

        if (starts[0] != 0)
        {
            throw new InvalidDataException($"Shapefile {geometryName} first part must start at point zero.");
        }

        for (var index = 0; index < starts.Length - 1; index++)
        {
            if (starts[index] < 0 || starts[index] > pointCount || starts[index] > starts[index + 1])
            {
                throw new InvalidDataException($"Shapefile {geometryName} contains an invalid part index at {index}.");
            }
        }
    }

    private static GisBoundingBox CreateDeclaredBounds(
        ReadOnlySpan<byte> content,
        double? minZ,
        double? maxZ)
    {
        EnsureAvailable(content, 4, 32, "shape bounding box");
        return new GisBoundingBox(
            new Envelope2D(
                ShapefileBinary.ReadDoubleLittleEndian(content, 4),
                ShapefileBinary.ReadDoubleLittleEndian(content, 12),
                ShapefileBinary.ReadDoubleLittleEndian(content, 20),
                ShapefileBinary.ReadDoubleLittleEndian(content, 28)),
            minZ,
            maxZ);
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

    private sealed record ParsedParts(
        List<IReadOnlyList<GisCoordinate>> Parts,
        double? MinZ,
        double? MaxZ);
}
