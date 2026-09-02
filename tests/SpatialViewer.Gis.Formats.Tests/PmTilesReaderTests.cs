using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SpatialViewer.Formats.Gis.PmTiles;
using SpatialViewer.Gis.Core;
using Xunit;

namespace SpatialViewer.Gis.Formats.Tests;

public sealed class PmTilesReaderTests
{
    [Theory]
    [InlineData("sample.pmtiles", true)]
    [InlineData("sample.PMTILES", true)]
    [InlineData("sample.mbtiles", false)]
    public async Task ProbeUsesPmTilesExtension(string path, bool expected)
    {
        var result = await new PmTilesFormatProbe().ProbeAsync(path);
        Assert.Equal(expected, result.IsMatch);
    }

    [Fact]
    public async Task ReadsLocalMetadataAndHilbertAddressedPngTile()
    {
        var archive = BuildArchive(
            zoom: 1,
            x: 1,
            y: 0,
            tileType: 2,
            tileCompression: 1,
            tilePayload: new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 });
        var path = Path.Combine(Path.GetTempPath(), $"SpatialViewer-{Guid.NewGuid():N}.pmtiles");
        await File.WriteAllBytesAsync(path, archive);
        try
        {
            using var reader = new PmTilesDataSourceReader();
            var metadata = await reader.ReadMetadataAsync(path);
            var tile = await reader.ReadTileAsync(path, "tiles", new TileCoordinate(1, 1, 0));
            var missing = await reader.ReadTileAsync(path, "tiles", new TileCoordinate(1, 0, 0));

            Assert.Equal("Synthetic PMTiles", metadata.Name);
            Assert.Equal(TileScheme.Xyz, metadata.StorageScheme);
            Assert.Equal(1, metadata.MinimumZoom);
            Assert.Equal(1, metadata.MaximumZoom);
            Assert.Equal(TileContentType.Png, metadata.ContentType);
            Assert.Equal(SpatialReference.FromEpsg(3857), metadata.SpatialReference);
            Assert.Equal(new Envelope2D(-10, -5, 10, 5), metadata.GeographicBounds);
            Assert.Equal("Synthetic attribution", metadata.Attribution);
            Assert.NotNull(tile);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 }, tile.Content.ToArray());
            Assert.Null(missing);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DecompressesGzipMvtTileBeforeReturningPayload()
    {
        var raw = new byte[] { 0x1A, 0x02, 0x08, 0x01 };
        var archive = BuildArchive(0, 0, 0, tileType: 1, tileCompression: 2, tilePayload: raw);
        var path = Path.Combine(Path.GetTempPath(), $"SpatialViewer-{Guid.NewGuid():N}.pmtiles");
        await File.WriteAllBytesAsync(path, archive);
        try
        {
            using var reader = new PmTilesDataSourceReader();
            var tile = await reader.ReadTileAsync(path, "tiles", new TileCoordinate(0, 0, 0));

            Assert.NotNull(tile);
            Assert.Equal(TileContentType.VectorPbf, tile.ContentType);
            Assert.Equal(TilePayloadKind.VectorTile, tile.PayloadKind);
            Assert.Equal(raw, tile.Content.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RemoteArchiveUsesOnlyHttpRangeRequests()
    {
        var archive = BuildArchive(
            1,
            1,
            0,
            tileType: 2,
            tileCompression: 1,
            tilePayload: new byte[] { 0x89, 0x50, 0x4E, 0x47, 9, 8, 7, 6 });
        var handler = new RangeArchiveHandler(archive);
        using var httpClient = new HttpClient(handler);
        using var reader = new PmTilesDataSourceReader(httpClient);

        var metadata = await reader.ReadMetadataAsync("https://example.test/synthetic.pmtiles");
        var tile = await reader.ReadTileAsync(
            "https://example.test/synthetic.pmtiles",
            "tiles",
            new TileCoordinate(1, 1, 0));

        Assert.Equal("Synthetic PMTiles", metadata.Name);
        Assert.NotNull(tile);
        Assert.True(handler.RequestCount >= 5);
        Assert.All(handler.RequestedRanges, range => Assert.True(range.Length < archive.Length));
    }

    [Fact]
    public async Task RejectsRemoteServerThatIgnoresRangeRequests()
    {
        var archive = BuildArchive(0, 0, 0, tileType: 2, tileCompression: 1, tilePayload: new byte[] { 1, 2, 3, 4 });
        using var httpClient = new HttpClient(new IgnoreRangeHandler(archive));
        using var reader = new PmTilesDataSourceReader(httpClient);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            async () => await reader.ReadMetadataAsync("https://example.test/synthetic.pmtiles"));

        Assert.Contains("206 Partial Content", exception.Message, StringComparison.Ordinal);
    }

    private static byte[] BuildArchive(
        int zoom,
        int x,
        int y,
        byte tileType,
        byte tileCompression,
        byte[] tilePayload)
    {
        var encodedTile = tileCompression == 2 ? Gzip(tilePayload) : tilePayload;
        var tileId = ZxyToTileId(zoom, x, y);
        var directory = new List<byte>();
        WriteVarInt(directory, 1);
        WriteVarInt(directory, tileId);
        WriteVarInt(directory, 1);
        WriteVarInt(directory, checked((ulong)encodedTile.Length));
        WriteVarInt(directory, 1);

        var metadata = Encoding.UTF8.GetBytes("{\"name\":\"Synthetic PMTiles\",\"attribution\":\"Synthetic attribution\"}");
        var rootOffset = 127UL;
        var rootLength = checked((ulong)directory.Count);
        var metadataOffset = checked(rootOffset + rootLength);
        var metadataLength = checked((ulong)metadata.Length);
        var leafOffset = checked(metadataOffset + metadataLength);
        var tileDataOffset = leafOffset;
        var header = new byte[127];
        "PMTiles"u8.CopyTo(header);
        header[7] = 3;
        WriteUInt64(header, 8, rootOffset);
        WriteUInt64(header, 16, rootLength);
        WriteUInt64(header, 24, metadataOffset);
        WriteUInt64(header, 32, metadataLength);
        WriteUInt64(header, 40, leafOffset);
        WriteUInt64(header, 48, 0);
        WriteUInt64(header, 56, tileDataOffset);
        WriteUInt64(header, 64, checked((ulong)encodedTile.Length));
        WriteUInt64(header, 72, 1);
        WriteUInt64(header, 80, 1);
        WriteUInt64(header, 88, 1);
        header[96] = 1;
        header[97] = 1;
        header[98] = tileCompression;
        header[99] = tileType;
        header[100] = checked((byte)zoom);
        header[101] = checked((byte)zoom);
        WriteInt32(header, 102, -100_000_000);
        WriteInt32(header, 106, -50_000_000);
        WriteInt32(header, 110, 100_000_000);
        WriteInt32(header, 114, 50_000_000);
        header[118] = checked((byte)zoom);
        WriteInt32(header, 119, 0);
        WriteInt32(header, 123, 0);

        using var output = new MemoryStream();
        output.Write(header);
        output.Write(directory.ToArray());
        output.Write(metadata);
        output.Write(encodedTile);
        return output.ToArray();
    }

    private static ulong ZxyToTileId(int zoom, int x, int y)
    {
        var accumulated = ((1UL << (zoom * 2)) - 1UL) / 3UL;
        for (var bit = zoom - 1; bit >= 0; bit--)
        {
            var scale = 1 << bit;
            var rx = x & scale;
            var ry = y & scale;
            accumulated += (ulong)((3 * rx) ^ ry) << bit;
            if (ry == 0)
            {
                if (rx != 0)
                {
                    x = scale - 1 - x;
                    y = scale - 1 - y;
                }

                (x, y) = (y, x);
            }
        }

        return accumulated;
    }

    private static byte[] Gzip(byte[] input)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(input);
        }

        return output.ToArray();
    }

    private static void WriteVarInt(List<byte> output, ulong value)
    {
        do
        {
            var current = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
            {
                current |= 0x80;
            }

            output.Add(current);
        }
        while (value != 0);
    }

    private static void WriteUInt64(byte[] buffer, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(offset, sizeof(ulong)), value);

    private static void WriteInt32(byte[] buffer, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, sizeof(int)), value);

    private sealed class RangeArchiveHandler(byte[] archive) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public List<(long Start, long End, long Length)> RequestedRanges { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var range = Assert.Single(request.Headers.Range?.Ranges ?? Array.Empty<RangeItemHeaderValue>());
            var start = Assert.IsType<long>(range.From);
            var end = Assert.IsType<long>(range.To);
            Assert.InRange(start, 0, archive.LongLength - 1);
            Assert.InRange(end, start, archive.LongLength - 1);
            var length = checked((int)(end - start + 1));
            var payload = new byte[length];
            Buffer.BlockCopy(archive, checked((int)start), payload, 0, length);
            RequestCount++;
            RequestedRanges.Add((start, end, length));
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(payload),
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, archive.LongLength);
            return Task.FromResult(response);
        }
    }

    private sealed class IgnoreRangeHandler(byte[] archive) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archive),
            });
        }
    }
}
