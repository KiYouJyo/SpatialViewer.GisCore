using System.Buffers.Binary;
using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis.GeoPackage;

internal sealed record GeoPackageGeometryResult(
    IGisGeometry? Geometry,
    int SpatialReferenceId,
    GisBoundingBox? DeclaredBounds,
    bool IsEmpty);

internal static class GeoPackageGeometryReader
{
    private const byte MagicG = 0x47;
    private const byte MagicP = 0x50;

    public static GeoPackageGeometryResult Parse(ReadOnlySpan<byte> blob)
    {
        EnsureAvailable(blob, 0, 8, "GeoPackage geometry header");

        if (blob[0] != MagicG || blob[1] != MagicP)
        {
            throw new InvalidDataException("GeoPackage geometry blob does not start with the 'GP' magic bytes.");
        }

        if (blob[2] != 0)
        {
            throw new NotSupportedException($"GeoPackage geometry binary version {blob[2]} is not supported.");
        }

        var flags = blob[3];
        if ((flags & 0b1100_0000) != 0)
        {
            throw new InvalidDataException("GeoPackage geometry header uses reserved flag bits.");
        }

        if ((flags & 0b0010_0000) != 0)
        {
            throw new NotSupportedException("Extended GeoPackageBinary geometry types are not supported by the managed reference reader.");
        }

        var isEmpty = (flags & 0b0001_0000) != 0;
        var envelopeCode = (flags >> 1) & 0b0000_0111;
        var littleEndianHeader = (flags & 0b0000_0001) != 0;
        var spatialReferenceId = ReadInt32(blob, 4, littleEndianHeader);
        var envelopeLength = envelopeCode switch
        {
            0 => 0,
            1 => 32,
            2 or 3 => 48,
            4 => 64,
            _ => throw new InvalidDataException($"GeoPackage geometry envelope code {envelopeCode} is invalid."),
        };

        EnsureAvailable(blob, 8, envelopeLength, "GeoPackage geometry envelope");
        GisBoundingBox? declaredBounds = envelopeLength == 0
            ? null
            : ParseEnvelope(blob.Slice(8, envelopeLength), envelopeCode, littleEndianHeader);

        var wkbOffset = 8 + envelopeLength;
        EnsureAvailable(blob, wkbOffset, 5, "WKB geometry");
        var offset = wkbOffset;
        var geometry = ParseWkbGeometry(blob, ref offset);

        if (offset > blob.Length)
        {
            throw new InvalidDataException("GeoPackage WKB geometry extends beyond the blob boundary.");
        }

        if (isEmpty)
        {
            geometry = NormalizeEmptyGeometry(geometry);
        }

        geometry = ApplyDeclaredBounds(geometry, declaredBounds);
        return new GeoPackageGeometryResult(geometry, spatialReferenceId, declaredBounds, isEmpty);
    }

    private static IGisGeometry? ParseWkbGeometry(ReadOnlySpan<byte> blob, ref int offset)
    {
        EnsureAvailable(blob, offset, 5, "WKB geometry header");
        var littleEndian = blob[offset] switch
        {
            0 => false,
            1 => true,
            var value => throw new InvalidDataException($"WKB byte order value {value} is invalid."),
        };
        offset++;

        var rawType = ReadUInt32(blob, offset, littleEndian);
        offset += 4;
        var typeInfo = DecodeType(rawType);

        if (typeInfo.HasEmbeddedSrid)
        {
            EnsureAvailable(blob, offset, 4, "EWKB embedded SRID");
            offset += 4;
        }

        return typeInfo.BaseType switch
        {
            1 => ParsePoint(blob, ref offset, littleEndian, typeInfo.HasZ, typeInfo.HasM),
            2 => ParseLineString(blob, ref offset, littleEndian, typeInfo.HasZ, typeInfo.HasM),
            3 => ParsePolygon(blob, ref offset, littleEndian, typeInfo.HasZ, typeInfo.HasM),
            4 => ParseMultiPoint(blob, ref offset, littleEndian),
            5 => ParseMultiLineString(blob, ref offset, littleEndian),
            6 => ParseMultiPolygon(blob, ref offset, littleEndian),
            7 => ParseGeometryCollection(blob, ref offset, littleEndian),
            _ => throw new NotSupportedException($"WKB geometry type {typeInfo.BaseType} is not supported."),
        };
    }

    private static PointGeometry? ParsePoint(
        ReadOnlySpan<byte> blob,
        ref int offset,
        bool littleEndian,
        bool hasZ,
        bool hasM)
    {
        var coordinate = ReadCoordinate(blob, ref offset, littleEndian, hasZ, hasM);
        return double.IsNaN(coordinate.X) && double.IsNaN(coordinate.Y)
            ? null
            : new PointGeometry(coordinate);
    }

    private static LineStringGeometry ParseLineString(
        ReadOnlySpan<byte> blob,
        ref int offset,
        bool littleEndian,
        bool hasZ,
        bool hasM)
    {
        var count = ReadCount(blob, ref offset, littleEndian, "LineString point count");
        var coordinates = new List<GisCoordinate>(count);

        for (var index = 0; index < count; index++)
        {
            coordinates.Add(ReadCoordinate(blob, ref offset, littleEndian, hasZ, hasM));
        }

        return new LineStringGeometry(coordinates);
    }

    private static PolygonGeometry ParsePolygon(
        ReadOnlySpan<byte> blob,
        ref int offset,
        bool littleEndian,
        bool hasZ,
        bool hasM)
    {
        var ringCount = ReadCount(blob, ref offset, littleEndian, "Polygon ring count");
        var rings = new List<IReadOnlyList<GisCoordinate>>(ringCount);

        for (var ringIndex = 0; ringIndex < ringCount; ringIndex++)
        {
            var pointCount = ReadCount(blob, ref offset, littleEndian, "Polygon ring point count");
            var ring = new List<GisCoordinate>(pointCount);

            for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                ring.Add(ReadCoordinate(blob, ref offset, littleEndian, hasZ, hasM));
            }

            rings.Add(ring);
        }

        return new PolygonGeometry(rings);
    }

    private static MultiPointGeometry ParseMultiPoint(
        ReadOnlySpan<byte> blob,
        ref int offset,
        bool littleEndian)
    {
        var count = ReadCount(blob, ref offset, littleEndian, "MultiPoint geometry count");
        var coordinates = new List<GisCoordinate>(count);

        for (var index = 0; index < count; index++)
        {
            var child = ParseWkbGeometry(blob, ref offset);
            if (child is PointGeometry point)
            {
                coordinates.Add(point.Coordinate);
            }
            else if (child is not null)
            {
                throw new InvalidDataException("WKB MultiPoint contains a non-Point child geometry.");
            }
        }

        return new MultiPointGeometry(coordinates);
    }

    private static MultiLineStringGeometry ParseMultiLineString(
        ReadOnlySpan<byte> blob,
        ref int offset,
        bool littleEndian)
    {
        var count = ReadCount(blob, ref offset, littleEndian, "MultiLineString geometry count");
        var lines = new List<IReadOnlyList<GisCoordinate>>(count);

        for (var index = 0; index < count; index++)
        {
            var child = ParseWkbGeometry(blob, ref offset);
            if (child is not LineStringGeometry line)
            {
                throw new InvalidDataException("WKB MultiLineString contains a non-LineString child geometry.");
            }

            lines.Add(line.Coordinates);
        }

        return new MultiLineStringGeometry(lines);
    }

    private static MultiPolygonGeometry ParseMultiPolygon(
        ReadOnlySpan<byte> blob,
        ref int offset,
        bool littleEndian)
    {
        var count = ReadCount(blob, ref offset, littleEndian, "MultiPolygon geometry count");
        var polygons = new List<IReadOnlyList<IReadOnlyList<GisCoordinate>>>(count);

        for (var index = 0; index < count; index++)
        {
            var child = ParseWkbGeometry(blob, ref offset);
            if (child is not PolygonGeometry polygon)
            {
                throw new InvalidDataException("WKB MultiPolygon contains a non-Polygon child geometry.");
            }

            polygons.Add(polygon.Rings);
        }

        return new MultiPolygonGeometry(polygons);
    }

    private static GeometryCollectionGeometry ParseGeometryCollection(
        ReadOnlySpan<byte> blob,
        ref int offset,
        bool littleEndian)
    {
        var count = ReadCount(blob, ref offset, littleEndian, "GeometryCollection geometry count");
        var geometries = new List<IGisGeometry>(count);

        for (var index = 0; index < count; index++)
        {
            var child = ParseWkbGeometry(blob, ref offset);
            if (child is not null)
            {
                geometries.Add(child);
            }
        }

        return new GeometryCollectionGeometry(geometries);
    }

    private static GisCoordinate ReadCoordinate(
        ReadOnlySpan<byte> blob,
        ref int offset,
        bool littleEndian,
        bool hasZ,
        bool hasM)
    {
        var ordinateCount = 2 + (hasZ ? 1 : 0) + (hasM ? 1 : 0);
        EnsureAvailable(blob, offset, checked(ordinateCount * 8), "WKB coordinate");

        var x = ReadDouble(blob, offset, littleEndian);
        offset += 8;
        var y = ReadDouble(blob, offset, littleEndian);
        offset += 8;
        double? z = null;
        double? m = null;

        if (hasZ)
        {
            z = ReadDouble(blob, offset, littleEndian);
            offset += 8;
        }

        if (hasM)
        {
            m = ReadDouble(blob, offset, littleEndian);
            offset += 8;
        }

        return new GisCoordinate(x, y, z, m);
    }

    private static GisBoundingBox ParseEnvelope(
        ReadOnlySpan<byte> envelope,
        int envelopeCode,
        bool littleEndian)
    {
        var minX = ReadDouble(envelope, 0, littleEndian);
        var maxX = ReadDouble(envelope, 8, littleEndian);
        var minY = ReadDouble(envelope, 16, littleEndian);
        var maxY = ReadDouble(envelope, 24, littleEndian);
        double? minZ = null;
        double? maxZ = null;

        if (envelopeCode is 2 or 4)
        {
            minZ = ReadDouble(envelope, 32, littleEndian);
            maxZ = ReadDouble(envelope, 40, littleEndian);
        }

        return new GisBoundingBox(new Envelope2D(minX, minY, maxX, maxY), minZ, maxZ);
    }

    private static IGisGeometry? ApplyDeclaredBounds(IGisGeometry? geometry, GisBoundingBox? bounds) =>
        geometry switch
        {
            null => null,
            PointGeometry point => point with { DeclaredBounds = bounds },
            MultiPointGeometry multiPoint => multiPoint with { DeclaredBounds = bounds },
            LineStringGeometry line => line with { DeclaredBounds = bounds },
            MultiLineStringGeometry multiLine => multiLine with { DeclaredBounds = bounds },
            PolygonGeometry polygon => polygon with { DeclaredBounds = bounds },
            MultiPolygonGeometry multiPolygon => multiPolygon with { DeclaredBounds = bounds },
            GeometryCollectionGeometry collection => collection with { DeclaredBounds = bounds },
            _ => geometry,
        };

    private static IGisGeometry? NormalizeEmptyGeometry(IGisGeometry? geometry) => geometry switch
    {
        PointGeometry => null,
        LineStringGeometry => new LineStringGeometry(Array.Empty<GisCoordinate>()),
        MultiPointGeometry => new MultiPointGeometry(Array.Empty<GisCoordinate>()),
        MultiLineStringGeometry => new MultiLineStringGeometry(Array.Empty<IReadOnlyList<GisCoordinate>>()),
        PolygonGeometry => new PolygonGeometry(Array.Empty<IReadOnlyList<GisCoordinate>>()),
        MultiPolygonGeometry => new MultiPolygonGeometry(Array.Empty<IReadOnlyList<IReadOnlyList<GisCoordinate>>>()),
        GeometryCollectionGeometry => new GeometryCollectionGeometry(Array.Empty<IGisGeometry>()),
        null => null,
        _ => geometry,
    };

    private static WkbTypeInfo DecodeType(uint rawType)
    {
        var hasZ = (rawType & 0x8000_0000u) != 0;
        var hasM = (rawType & 0x4000_0000u) != 0;
        var hasEmbeddedSrid = (rawType & 0x2000_0000u) != 0;
        var baseType = rawType & 0x1FFF_FFFFu;

        if (baseType >= 3000 && baseType < 4000)
        {
            hasZ = true;
            hasM = true;
            baseType -= 3000;
        }
        else if (baseType >= 2000 && baseType < 3000)
        {
            hasM = true;
            baseType -= 2000;
        }
        else if (baseType >= 1000 && baseType < 2000)
        {
            hasZ = true;
            baseType -= 1000;
        }

        return new WkbTypeInfo(checked((int)baseType), hasZ, hasM, hasEmbeddedSrid);
    }

    private static int ReadCount(
        ReadOnlySpan<byte> blob,
        ref int offset,
        bool littleEndian,
        string description)
    {
        EnsureAvailable(blob, offset, 4, description);
        var count = checked((int)ReadUInt32(blob, offset, littleEndian));
        offset += 4;
        return count;
    }

    private static int ReadInt32(ReadOnlySpan<byte> value, int offset, bool littleEndian)
    {
        EnsureAvailable(value, offset, 4, "32-bit integer");
        return littleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(value.Slice(offset, 4))
            : BinaryPrimitives.ReadInt32BigEndian(value.Slice(offset, 4));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> value, int offset, bool littleEndian)
    {
        EnsureAvailable(value, offset, 4, "32-bit unsigned integer");
        return littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(offset, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(value.Slice(offset, 4));
    }

    private static double ReadDouble(ReadOnlySpan<byte> value, int offset, bool littleEndian)
    {
        EnsureAvailable(value, offset, 8, "64-bit floating-point value");
        var bits = littleEndian
            ? BinaryPrimitives.ReadInt64LittleEndian(value.Slice(offset, 8))
            : BinaryPrimitives.ReadInt64BigEndian(value.Slice(offset, 8));
        return BitConverter.Int64BitsToDouble(bits);
    }

    private static void EnsureAvailable(
        ReadOnlySpan<byte> value,
        int offset,
        int length,
        string description)
    {
        if (offset < 0 || length < 0 || value.Length - offset < length)
        {
            throw new InvalidDataException($"GeoPackage geometry blob is truncated while reading {description}.");
        }
    }

    private readonly record struct WkbTypeInfo(
        int BaseType,
        bool HasZ,
        bool HasM,
        bool HasEmbeddedSrid);
}
