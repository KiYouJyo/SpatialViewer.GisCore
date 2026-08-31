using System.Globalization;
using System.Text;
using SpatialViewer.Formats.Gis.Mvt;
using SpatialViewer.Gis.Core;
using Xunit;

namespace SpatialViewer.Gis.Formats.Tests;

public sealed class MvtDecoderTests
{
    [Fact]
    public void DecodesPointLinePolygonAttributesAndIdsIntoWebMercator()
    {
        var tileCoordinate = new TileCoordinate(1, 1, 1);
        var payload = CreateTile(
            "places",
            4096,
            new[] { "name" },
            new[] { CreateStringValue("center") },
            new[]
            {
                CreateFeature(7, new uint[] { 0, 0 }, 1, new uint[]
                {
                    Command(1, 1), ZigZag(2048), ZigZag(2048),
                }),
                CreateFeature(8, Array.Empty<uint>(), 2, new uint[]
                {
                    Command(1, 1), ZigZag(0), ZigZag(0),
                    Command(2, 2), ZigZag(4096), ZigZag(0), ZigZag(0), ZigZag(4096),
                }),
                CreateFeature(9, Array.Empty<uint>(), 3, new uint[]
                {
                    Command(1, 1), ZigZag(0), ZigZag(0),
                    Command(2, 3), ZigZag(4096), ZigZag(0), ZigZag(0), ZigZag(4096), ZigZag(-4096), ZigZag(0),
                    Command(7, 1),
                }),
            });
        var tile = new TileReadResult(tileCoordinate, TileContentType.VectorPbf, payload);

        var decoded = MvtTileDecoder.Decode(tile);
        var layer = Assert.Single(decoded.Layers);

        Assert.Equal("places", layer.Name);
        Assert.Equal(4096, layer.Extent);
        Assert.Equal(3, layer.Features.Count);

        var pointFeature = layer.Features[0];
        Assert.Equal("7", pointFeature.Id);
        Assert.Equal("center", pointFeature.Attributes["name"]);
        var point = Assert.IsType<PointGeometry>(pointFeature.Geometry);
        Assert.Equal(WebMercatorTileMath.MaximumCoordinate / 2d, point.Coordinate.X, 6);
        Assert.Equal(-WebMercatorTileMath.MaximumCoordinate / 2d, point.Coordinate.Y, 6);

        var line = Assert.IsType<LineStringGeometry>(layer.Features[1].Geometry);
        Assert.Equal(3, line.Coordinates.Count);
        Assert.Equal(0d, line.Coordinates[0].X, 6);
        Assert.Equal(0d, line.Coordinates[0].Y, 6);
        Assert.Equal(WebMercatorTileMath.MaximumCoordinate, line.Coordinates[2].X, 6);
        Assert.Equal(-WebMercatorTileMath.MaximumCoordinate, line.Coordinates[2].Y, 6);

        var polygon = Assert.IsType<PolygonGeometry>(layer.Features[2].Geometry);
        var ring = Assert.Single(polygon.Rings);
        Assert.Equal(5, ring.Count);
        Assert.Equal(ring[0], ring[^1]);
    }

    [Fact]
    public void DecodesMultipleExteriorRingsAsMultiPolygon()
    {
        var geometry = new uint[]
        {
            Command(1, 1), ZigZag(0), ZigZag(0),
            Command(2, 3), ZigZag(1000), ZigZag(0), ZigZag(0), ZigZag(1000), ZigZag(-1000), ZigZag(0),
            Command(7, 1),
            Command(1, 1), ZigZag(2000), ZigZag(1000),
            Command(2, 3), ZigZag(1000), ZigZag(0), ZigZag(0), ZigZag(1000), ZigZag(-1000), ZigZag(0),
            Command(7, 1),
        };
        var payload = CreateTile(
            "land",
            4096,
            Array.Empty<string>(),
            Array.Empty<byte[]>(),
            new[] { CreateFeature(1, Array.Empty<uint>(), 3, geometry) });
        var decoded = MvtTileDecoder.Decode(new TileReadResult(
            new TileCoordinate(0, 0, 0),
            TileContentType.VectorPbf,
            payload));

        var geometryResult = Assert.Single(Assert.Single(decoded.Layers).Features).Geometry;
        var multiPolygon = Assert.IsType<MultiPolygonGeometry>(geometryResult);
        Assert.Equal(2, multiPolygon.Polygons.Count);
    }

    [Fact]
    public void RejectsOutOfRangeTagIndex()
    {
        var payload = CreateTile(
            "broken",
            4096,
            new[] { "name" },
            new[] { CreateStringValue("value") },
            new[]
            {
                CreateFeature(1, new uint[] { 3, 0 }, 1, new uint[]
                {
                    Command(1, 1), ZigZag(0), ZigZag(0),
                }),
            });
        var tile = new TileReadResult(new TileCoordinate(0, 0, 0), TileContentType.VectorPbf, payload);

        var exception = Assert.Throws<InvalidDataException>(() => MvtTileDecoder.Decode(tile));

        Assert.Contains("out-of-range", exception.Message, StringComparison.Ordinal);
    }

    private static byte[] CreateTile(
        string layerName,
        uint extent,
        IReadOnlyList<string> keys,
        IReadOnlyList<byte[]> values,
        IReadOnlyList<byte[]> features)
    {
        var layer = new List<byte>();
        WriteString(layer, 1, layerName);
        foreach (var feature in features)
        {
            WriteBytes(layer, 2, feature);
        }

        foreach (var key in keys)
        {
            WriteString(layer, 3, key);
        }

        foreach (var value in values)
        {
            WriteBytes(layer, 4, value);
        }

        WriteVarintField(layer, 5, extent);
        WriteVarintField(layer, 15, 2);

        var tile = new List<byte>();
        WriteBytes(tile, 3, layer.ToArray());
        return tile.ToArray();
    }

    private static byte[] CreateFeature(
        ulong id,
        IReadOnlyList<uint> tags,
        uint geometryType,
        IReadOnlyList<uint> geometry)
    {
        var feature = new List<byte>();
        WriteVarintField(feature, 1, id);
        if (tags.Count > 0)
        {
            WritePackedUInt32(feature, 2, tags);
        }

        WriteVarintField(feature, 3, geometryType);
        WritePackedUInt32(feature, 4, geometry);
        return feature.ToArray();
    }

    private static byte[] CreateStringValue(string value)
    {
        var result = new List<byte>();
        WriteString(result, 1, value);
        return result.ToArray();
    }

    private static uint Command(uint id, uint count) => (count << 3) | id;

    private static uint ZigZag(int value) => unchecked((uint)((value << 1) ^ (value >> 31)));

    private static void WritePackedUInt32(List<byte> destination, int fieldNumber, IReadOnlyList<uint> values)
    {
        var packed = new List<byte>();
        foreach (var value in values)
        {
            WriteVarint(packed, value);
        }

        WriteBytes(destination, fieldNumber, packed.ToArray());
    }

    private static void WriteString(List<byte> destination, int fieldNumber, string value) =>
        WriteBytes(destination, fieldNumber, Encoding.UTF8.GetBytes(value));

    private static void WriteBytes(List<byte> destination, int fieldNumber, byte[] value)
    {
        WriteVarint(destination, checked((ulong)((fieldNumber << 3) | 2)));
        WriteVarint(destination, checked((ulong)value.Length));
        destination.AddRange(value);
    }

    private static void WriteVarintField(List<byte> destination, int fieldNumber, ulong value)
    {
        WriteVarint(destination, checked((ulong)(fieldNumber << 3)));
        WriteVarint(destination, value);
    }

    private static void WriteVarint(List<byte> destination, ulong value)
    {
        while (value >= 0x80)
        {
            destination.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        destination.Add((byte)value);
    }
}
