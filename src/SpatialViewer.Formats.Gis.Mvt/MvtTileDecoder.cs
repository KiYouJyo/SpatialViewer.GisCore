using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis.Mvt;

public sealed record MvtDecodedLayer(
    string Name,
    int Extent,
    IReadOnlyList<GisFeature> Features);

public sealed record MvtDecodedTile(
    TileCoordinate Coordinate,
    IReadOnlyList<MvtDecodedLayer> Layers)
{
    public SpatialReference TileSpatialReference => SpatialReference.FromEpsg(3857);
}

public static class MvtTileDecoder
{
    public static MvtDecodedTile Decode(TileReadResult tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (tile.ContentType != TileContentType.VectorPbf)
        {
            throw new ArgumentException("MVT decoder requires a VectorPbf tile payload.", nameof(tile));
        }

        var reader = new ProtoReader(tile.Content);
        var layers = new List<MvtDecodedLayer>();
        while (!reader.End)
        {
            var key = reader.ReadKey();
            if (key.FieldNumber == 3 && key.WireType == 2)
            {
                layers.Add(ParseLayer(reader.ReadLengthDelimited(), tile.Coordinate));
            }
            else
            {
                reader.SkipField(key.WireType);
            }
        }

        return new MvtDecodedTile(tile.Coordinate, layers.ToArray());
    }

    private static MvtDecodedLayer ParseLayer(
        ReadOnlyMemory<byte> payload,
        TileCoordinate coordinate)
    {
        var reader = new ProtoReader(payload);
        string? name = null;
        var featurePayloads = new List<ReadOnlyMemory<byte>>();
        var keys = new List<string>();
        var values = new List<object?>();
        var extent = 4096;
        uint? version = null;

        while (!reader.End)
        {
            var key = reader.ReadKey();
            switch (key.FieldNumber, key.WireType)
            {
                case (1, 2):
                    name = reader.ReadString();
                    break;
                case (2, 2):
                    featurePayloads.Add(reader.ReadLengthDelimited());
                    break;
                case (3, 2):
                    keys.Add(reader.ReadString());
                    break;
                case (4, 2):
                    values.Add(ParseValue(reader.ReadLengthDelimited()));
                    break;
                case (5, 0):
                    extent = reader.ReadInt32Checked("MVT layer extent");
                    break;
                case (15, 0):
                    version = reader.ReadUInt32Checked("MVT layer version");
                    break;
                default:
                    reader.SkipField(key.WireType);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidDataException("MVT layer is missing a non-empty name.");
        }

        if (extent <= 0)
        {
            throw new InvalidDataException($"MVT layer '{name}' has invalid extent {extent}.");
        }

        if (version is not null && version is not (1 or 2))
        {
            throw new NotSupportedException($"MVT layer '{name}' uses unsupported version {version}.");
        }

        var features = new GisFeature[featurePayloads.Count];
        for (var index = 0; index < featurePayloads.Count; index++)
        {
            features[index] = ParseFeature(featurePayloads[index], keys, values, extent, coordinate, name);
        }

        return new MvtDecodedLayer(name, extent, features);
    }

    private static GisFeature ParseFeature(
        ReadOnlyMemory<byte> payload,
        IReadOnlyList<string> keys,
        IReadOnlyList<object?> values,
        int extent,
        TileCoordinate coordinate,
        string layerName)
    {
        var reader = new ProtoReader(payload);
        ulong? id = null;
        var tags = new List<uint>();
        uint geometryType = 0;
        var geometry = new List<uint>();

        while (!reader.End)
        {
            var key = reader.ReadKey();
            switch (key.FieldNumber)
            {
                case 1 when key.WireType == 0:
                    id = reader.ReadVarint();
                    break;
                case 2 when key.WireType == 2:
                    ReadPackedUInt32(reader.ReadLengthDelimited(), tags, "MVT feature tags");
                    break;
                case 2 when key.WireType == 0:
                    tags.Add(reader.ReadUInt32Checked("MVT feature tag"));
                    break;
                case 3 when key.WireType == 0:
                    geometryType = reader.ReadUInt32Checked("MVT geometry type");
                    break;
                case 4 when key.WireType == 2:
                    ReadPackedUInt32(reader.ReadLengthDelimited(), geometry, "MVT geometry command");
                    break;
                case 4 when key.WireType == 0:
                    geometry.Add(reader.ReadUInt32Checked("MVT geometry command"));
                    break;
                default:
                    reader.SkipField(key.WireType);
                    break;
            }
        }

        if ((tags.Count & 1) != 0)
        {
            throw new InvalidDataException($"MVT feature in layer '{layerName}' has an odd tag index count.");
        }

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var tagIndex = 0; tagIndex < tags.Count; tagIndex += 2)
        {
            var keyIndex = tags[tagIndex];
            var valueIndex = tags[tagIndex + 1];
            if (keyIndex >= keys.Count || valueIndex >= values.Count)
            {
                throw new InvalidDataException($"MVT feature in layer '{layerName}' references an out-of-range key/value index.");
            }

            attributes[keys[checked((int)keyIndex)]] = values[checked((int)valueIndex)];
        }

        var decodedGeometry = geometryType switch
        {
            0 => null,
            1 => DecodePointGeometry(geometry, extent, coordinate, layerName),
            2 => DecodeLineGeometry(geometry, extent, coordinate, layerName),
            3 => DecodePolygonGeometry(geometry, extent, coordinate, layerName),
            _ => throw new InvalidDataException($"MVT feature in layer '{layerName}' uses invalid geometry type {geometryType}."),
        };
        var idText = id?.ToString(CultureInfo.InvariantCulture);
        return new GisFeature(idText, decodedGeometry, attributes);
    }

    private static object? ParseValue(ReadOnlyMemory<byte> payload)
    {
        var reader = new ProtoReader(payload);
        object? value = null;
        var hasValue = false;
        while (!reader.End)
        {
            var key = reader.ReadKey();
            object? current;
            switch (key.FieldNumber, key.WireType)
            {
                case (1, 2):
                    current = reader.ReadString();
                    break;
                case (2, 5):
                    current = reader.ReadFloat();
                    break;
                case (3, 1):
                    current = reader.ReadDouble();
                    break;
                case (4, 0):
                    current = unchecked((long)reader.ReadVarint());
                    break;
                case (5, 0):
                    current = reader.ReadVarint();
                    break;
                case (6, 0):
                    current = DecodeZigZag(reader.ReadVarint());
                    break;
                case (7, 0):
                    current = reader.ReadVarint() != 0;
                    break;
                default:
                    reader.SkipField(key.WireType);
                    continue;
            }

            if (hasValue)
            {
                throw new InvalidDataException("MVT value message contains more than one typed value.");
            }

            value = current;
            hasValue = true;
        }

        if (!hasValue)
        {
            throw new InvalidDataException("MVT value message does not contain a typed value.");
        }

        return value;
    }

    private static IGisGeometry DecodePointGeometry(
        IReadOnlyList<uint> commands,
        int extent,
        TileCoordinate coordinate,
        string layerName)
    {
        var points = new List<LocalCoordinate>();
        long x = 0;
        long y = 0;
        var index = 0;
        while (index < commands.Count)
        {
            var commandInteger = commands[index++];
            var commandId = commandInteger & 0x7;
            var count = commandInteger >> 3;
            if (commandId != 1 || count == 0)
            {
                throw new InvalidDataException($"MVT point feature in layer '{layerName}' contains an invalid geometry command.");
            }

            for (var pointIndex = 0u; pointIndex < count; pointIndex++)
            {
                ReadDelta(commands, ref index, ref x, ref y, layerName);
                points.Add(new LocalCoordinate(x, y));
            }
        }

        if (points.Count == 0)
        {
            throw new InvalidDataException($"MVT point feature in layer '{layerName}' contains no points.");
        }

        var worldPoints = points.Select(point => ToWorld(point, extent, coordinate)).ToArray();
        return worldPoints.Length == 1
            ? new PointGeometry(worldPoints[0])
            : new MultiPointGeometry(worldPoints);
    }

    private static IGisGeometry DecodeLineGeometry(
        IReadOnlyList<uint> commands,
        int extent,
        TileCoordinate coordinate,
        string layerName)
    {
        var paths = new List<List<LocalCoordinate>>();
        List<LocalCoordinate>? current = null;
        long x = 0;
        long y = 0;
        var index = 0;
        while (index < commands.Count)
        {
            var commandInteger = commands[index++];
            var commandId = commandInteger & 0x7;
            var count = commandInteger >> 3;
            if (count == 0)
            {
                throw new InvalidDataException($"MVT line feature in layer '{layerName}' contains a zero-count command.");
            }

            if (commandId == 1)
            {
                if (count != 1)
                {
                    throw new InvalidDataException($"MVT line feature in layer '{layerName}' must use one MoveTo per path.");
                }

                if (current is not null)
                {
                    ValidateLine(current, layerName);
                }

                current = new List<LocalCoordinate>();
                paths.Add(current);
                ReadDelta(commands, ref index, ref x, ref y, layerName);
                current.Add(new LocalCoordinate(x, y));
            }
            else if (commandId == 2)
            {
                if (current is null)
                {
                    throw new InvalidDataException($"MVT line feature in layer '{layerName}' uses LineTo before MoveTo.");
                }

                for (var lineIndex = 0u; lineIndex < count; lineIndex++)
                {
                    ReadDelta(commands, ref index, ref x, ref y, layerName);
                    current.Add(new LocalCoordinate(x, y));
                }
            }
            else
            {
                throw new InvalidDataException($"MVT line feature in layer '{layerName}' contains unsupported command {commandId}.");
            }
        }

        if (current is not null)
        {
            ValidateLine(current, layerName);
        }

        if (paths.Count == 0)
        {
            throw new InvalidDataException($"MVT line feature in layer '{layerName}' contains no paths.");
        }

        var worldPaths = paths
            .Select(path => (IReadOnlyList<GisCoordinate>)path.Select(point => ToWorld(point, extent, coordinate)).ToArray())
            .ToArray();
        return worldPaths.Length == 1
            ? new LineStringGeometry(worldPaths[0])
            : new MultiLineStringGeometry(worldPaths);
    }

    private static IGisGeometry DecodePolygonGeometry(
        IReadOnlyList<uint> commands,
        int extent,
        TileCoordinate coordinate,
        string layerName)
    {
        var rings = new List<List<LocalCoordinate>>();
        List<LocalCoordinate>? current = null;
        long x = 0;
        long y = 0;
        var index = 0;
        while (index < commands.Count)
        {
            var commandInteger = commands[index++];
            var commandId = commandInteger & 0x7;
            var count = commandInteger >> 3;
            if (count == 0)
            {
                throw new InvalidDataException($"MVT polygon feature in layer '{layerName}' contains a zero-count command.");
            }

            if (commandId == 1)
            {
                if (count != 1 || current is not null)
                {
                    throw new InvalidDataException($"MVT polygon feature in layer '{layerName}' has an invalid MoveTo sequence.");
                }

                current = new List<LocalCoordinate>();
                ReadDelta(commands, ref index, ref x, ref y, layerName);
                current.Add(new LocalCoordinate(x, y));
            }
            else if (commandId == 2)
            {
                if (current is null)
                {
                    throw new InvalidDataException($"MVT polygon feature in layer '{layerName}' uses LineTo before MoveTo.");
                }

                for (var lineIndex = 0u; lineIndex < count; lineIndex++)
                {
                    ReadDelta(commands, ref index, ref x, ref y, layerName);
                    current.Add(new LocalCoordinate(x, y));
                }
            }
            else if (commandId == 7)
            {
                if (count != 1 || current is null)
                {
                    throw new InvalidDataException($"MVT polygon feature in layer '{layerName}' has an invalid ClosePath sequence.");
                }

                if (current.Count < 3)
                {
                    throw new InvalidDataException($"MVT polygon ring in layer '{layerName}' has fewer than three distinct vertices.");
                }

                current.Add(current[0]);
                rings.Add(current);
                current = null;
            }
            else
            {
                throw new InvalidDataException($"MVT polygon feature in layer '{layerName}' contains unsupported command {commandId}.");
            }
        }

        if (current is not null)
        {
            throw new InvalidDataException($"MVT polygon feature in layer '{layerName}' ended before ClosePath.");
        }

        if (rings.Count == 0)
        {
            throw new InvalidDataException($"MVT polygon feature in layer '{layerName}' contains no rings.");
        }

        var polygons = new List<List<List<LocalCoordinate>>>();
        List<List<LocalCoordinate>>? polygon = null;
        foreach (var ring in rings)
        {
            var area = SignedArea(ring);
            if (Math.Abs(area) <= double.Epsilon)
            {
                throw new InvalidDataException($"MVT polygon feature in layer '{layerName}' contains a zero-area ring.");
            }

            if (area > 0)
            {
                polygon = new List<List<LocalCoordinate>> { ring };
                polygons.Add(polygon);
            }
            else
            {
                if (polygon is null)
                {
                    throw new InvalidDataException($"MVT polygon feature in layer '{layerName}' starts with an interior ring.");
                }

                polygon.Add(ring);
            }
        }

        var worldPolygons = polygons
            .Select(item => (IReadOnlyList<IReadOnlyList<GisCoordinate>>)item
                .Select(ring => (IReadOnlyList<GisCoordinate>)ring.Select(point => ToWorld(point, extent, coordinate)).ToArray())
                .ToArray())
            .ToArray();
        return worldPolygons.Length == 1
            ? new PolygonGeometry(worldPolygons[0])
            : new MultiPolygonGeometry(worldPolygons);
    }

    private static void ValidateLine(IReadOnlyCollection<LocalCoordinate> line, string layerName)
    {
        if (line.Count < 2)
        {
            throw new InvalidDataException($"MVT line path in layer '{layerName}' contains fewer than two points.");
        }
    }

    private static void ReadDelta(
        IReadOnlyList<uint> commands,
        ref int index,
        ref long x,
        ref long y,
        string layerName)
    {
        if (index > commands.Count - 2)
        {
            throw new InvalidDataException($"MVT geometry in layer '{layerName}' ends inside a coordinate delta pair.");
        }

        x = checked(x + DecodeZigZag(commands[index++]));
        y = checked(y + DecodeZigZag(commands[index++]));
    }

    private static GisCoordinate ToWorld(
        LocalCoordinate coordinate,
        int extent,
        TileCoordinate tileCoordinate)
    {
        var bounds = WebMercatorTileMath.GetBounds(tileCoordinate);
        var width = bounds.MaxX - bounds.MinX;
        var height = bounds.MaxY - bounds.MinY;
        return new GisCoordinate(
            bounds.MinX + ((double)coordinate.X / extent * width),
            bounds.MaxY - ((double)coordinate.Y / extent * height));
    }

    private static double SignedArea(IReadOnlyList<LocalCoordinate> ring)
    {
        double sum = 0;
        for (var index = 0; index < ring.Count - 1; index++)
        {
            var current = ring[index];
            var next = ring[index + 1];
            sum += ((double)current.X * next.Y) - ((double)next.X * current.Y);
        }

        return sum / 2d;
    }

    private static void ReadPackedUInt32(
        ReadOnlyMemory<byte> payload,
        ICollection<uint> destination,
        string context)
    {
        var reader = new ProtoReader(payload);
        while (!reader.End)
        {
            destination.Add(reader.ReadUInt32Checked(context));
        }
    }

    private static long DecodeZigZag(ulong value) =>
        unchecked((long)(value >> 1) ^ -((long)value & 1));

    private static long DecodeZigZag(uint value) => DecodeZigZag((ulong)value);

    private readonly record struct LocalCoordinate(long X, long Y);

    private readonly record struct ProtoKey(int FieldNumber, int WireType);

    private sealed class ProtoReader
    {
        private readonly ReadOnlyMemory<byte> _content;
        private int _offset;

        public ProtoReader(ReadOnlyMemory<byte> content)
        {
            _content = content;
        }

        public bool End => _offset == _content.Length;

        public ProtoKey ReadKey()
        {
            var value = ReadVarint();
            var fieldNumber = checked((int)(value >> 3));
            var wireType = checked((int)(value & 0x7));
            if (fieldNumber <= 0)
            {
                throw new InvalidDataException("Protocol Buffer field number must be positive.");
            }

            return new ProtoKey(fieldNumber, wireType);
        }

        public ulong ReadVarint()
        {
            ulong result = 0;
            for (var shift = 0; shift < 70; shift += 7)
            {
                if (_offset >= _content.Length)
                {
                    throw new InvalidDataException("Protocol Buffer varint ended unexpectedly.");
                }

                var value = _content.Span[_offset++];
                if (shift == 63 && (value & 0xFE) != 0)
                {
                    throw new InvalidDataException("Protocol Buffer varint exceeds 64 bits.");
                }

                result |= (ulong)(value & 0x7F) << shift;
                if ((value & 0x80) == 0)
                {
                    return result;
                }
            }

            throw new InvalidDataException("Protocol Buffer varint exceeds 64 bits.");
        }

        public uint ReadUInt32Checked(string context)
        {
            var value = ReadVarint();
            if (value > uint.MaxValue)
            {
                throw new InvalidDataException($"{context} exceeds UInt32 range.");
            }

            return (uint)value;
        }

        public int ReadInt32Checked(string context)
        {
            var value = ReadVarint();
            if (value > int.MaxValue)
            {
                throw new InvalidDataException($"{context} exceeds Int32 range.");
            }

            return (int)value;
        }

        public ReadOnlyMemory<byte> ReadLengthDelimited()
        {
            var lengthValue = ReadVarint();
            if (lengthValue > int.MaxValue)
            {
                throw new InvalidDataException("Protocol Buffer length exceeds supported Int32 range.");
            }

            var length = (int)lengthValue;
            if (length > _content.Length - _offset)
            {
                throw new InvalidDataException("Protocol Buffer length-delimited field exceeds the remaining payload.");
            }

            var result = _content.Slice(_offset, length);
            _offset += length;
            return result;
        }

        public string ReadString() => Encoding.UTF8.GetString(ReadLengthDelimited().Span);

        public float ReadFloat()
        {
            EnsureRemaining(4);
            var bits = BinaryPrimitives.ReadInt32LittleEndian(_content.Span.Slice(_offset, 4));
            _offset += 4;
            return BitConverter.Int32BitsToSingle(bits);
        }

        public double ReadDouble()
        {
            EnsureRemaining(8);
            var bits = BinaryPrimitives.ReadInt64LittleEndian(_content.Span.Slice(_offset, 8));
            _offset += 8;
            return BitConverter.Int64BitsToDouble(bits);
        }

        public void SkipField(int wireType)
        {
            switch (wireType)
            {
                case 0:
                    ReadVarint();
                    return;
                case 1:
                    EnsureRemaining(8);
                    _offset += 8;
                    return;
                case 2:
                    ReadLengthDelimited();
                    return;
                case 5:
                    EnsureRemaining(4);
                    _offset += 4;
                    return;
                default:
                    throw new NotSupportedException($"Protocol Buffer wire type {wireType} is not supported.");
            }
        }

        private void EnsureRemaining(int count)
        {
            if (count > _content.Length - _offset)
            {
                throw new InvalidDataException("Protocol Buffer fixed-width value exceeds the remaining payload.");
            }
        }
    }
}
