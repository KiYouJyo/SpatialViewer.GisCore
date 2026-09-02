using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SpatialViewer.Formats.Gis;
using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis.PmTiles;

public sealed class PmTilesFormatProbe : IGisFormatProbe
{
    public const string FormatId = "pmtiles";

    public ValueTask<GisFormatProbeResult> ProbeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var candidate = path;
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            candidate = uri.AbsolutePath;
        }

        var match = string.Equals(Path.GetExtension(candidate), ".pmtiles", StringComparison.OrdinalIgnoreCase);
        return ValueTask.FromResult(match
            ? new GisFormatProbeResult(true, FormatId, 100)
            : GisFormatProbeResult.NoMatch);
    }
}

public sealed class PmTilesDataSourceReader : ITileDataSourceReader, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly TimeSpan _requestTimeout;
    private bool _disposed;

    public PmTilesDataSourceReader(TimeSpan? requestTimeout = null)
        : this(new HttpClient(), true, requestTimeout)
    {
    }

    public PmTilesDataSourceReader(HttpClient httpClient, TimeSpan? requestTimeout = null)
        : this(httpClient, false, requestTimeout)
    {
    }

    private PmTilesDataSourceReader(HttpClient httpClient, bool ownsHttpClient, TimeSpan? requestTimeout)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        var effectiveTimeout = requestTimeout ?? TimeSpan.FromSeconds(15);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(effectiveTimeout, TimeSpan.Zero);
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _requestTimeout = effectiveTimeout;
    }

    public string FormatId => PmTilesFormatProbe.FormatId;

    public async ValueTask<TileSourceMetadata> ReadMetadataAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await using var byteSource = CreateSource(source);
        var header = await ReadHeaderAsync(byteSource, cancellationToken).ConfigureAwait(false);
        var metadata = await ReadJsonMetadataAsync(byteSource, header, cancellationToken).ConfigureAwait(false);
        var name = TryGetString(metadata, "name") ?? GetDisplayName(source);
        var result = new TileSourceMetadata(
            name,
            TileScheme.Xyz,
            header.MinimumZoom,
            header.MaximumZoom,
            256,
            SpatialReference.FromEpsg(3857),
            MapContentType(header.TileType))
        {
            GeographicBounds = new Envelope2D(header.MinimumLongitude, header.MinimumLatitude, header.MaximumLongitude, header.MaximumLatitude),
            Attribution = TryGetString(metadata, "attribution"),
        };
        return result;
    }

    public async ValueTask<TileReadResult?> ReadTileAsync(
        string source,
        string layerName,
        TileCoordinate coordinate,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
        if (!string.Equals(layerName, "tiles", StringComparison.Ordinal))
        {
            throw new ArgumentException($"PMTiles layer '{layerName}' does not exist. Expected 'tiles'.", nameof(layerName));
        }

        if (!coordinate.IsValid)
        {
            throw new ArgumentException("Tile coordinate must be valid.", nameof(coordinate));
        }

        await using var byteSource = CreateSource(source);
        var header = await ReadHeaderAsync(byteSource, cancellationToken).ConfigureAwait(false);
        if (coordinate.Zoom < header.MinimumZoom || coordinate.Zoom > header.MaximumZoom)
        {
            return null;
        }

        var tileId = PmTilesTileId.FromZxy(coordinate);
        var directoryOffset = header.RootDirectoryOffset;
        var directoryLength = header.RootDirectoryLength;
        for (var depth = 0; depth <= 3; depth++)
        {
            var encodedDirectory = await byteSource.GetBytesAsync(
                directoryOffset,
                CheckedLength(directoryLength, "PMTiles directory"),
                cancellationToken).ConfigureAwait(false);
            var directoryBytes = Decompress(encodedDirectory, header.InternalCompression, "PMTiles directory");
            var entries = PmTilesDirectory.Decode(directoryBytes);
            var entry = PmTilesDirectory.Find(entries, tileId);
            if (entry is null)
            {
                return null;
            }

            if (entry.Value.RunLength > 0)
            {
                var encodedTile = await byteSource.GetBytesAsync(
                    checked(header.TileDataOffset + entry.Value.Offset),
                    CheckedLength(entry.Value.Length, "PMTiles tile"),
                    cancellationToken).ConfigureAwait(false);
                var tileBytes = Decompress(encodedTile, header.TileCompression, "PMTiles tile");
                if (tileBytes.Length == 0)
                {
                    throw new InvalidDataException("PMTiles tile payload is empty.");
                }

                return new TileReadResult(coordinate, MapContentType(header.TileType), tileBytes);
            }

            directoryOffset = checked(header.LeafDirectoryOffset + entry.Value.Offset);
            directoryLength = entry.Value.Length;
        }

        throw new InvalidDataException("PMTiles archive exceeded the supported maximum of four directory levels.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private IPmTilesByteSource CreateSource(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return new HttpPmTilesByteSource(_httpClient, uri, _requestTimeout);
        }

        return new FilePmTilesByteSource(Path.GetFullPath(source));
    }

    private static async ValueTask<PmTilesHeader> ReadHeaderAsync(
        IPmTilesByteSource source,
        CancellationToken cancellationToken)
    {
        var bytes = await source.GetBytesAsync(0, PmTilesHeader.HeaderSize, cancellationToken).ConfigureAwait(false);
        return PmTilesHeader.Parse(bytes);
    }

    private static async ValueTask<JsonDocument?> ReadJsonMetadataAsync(
        IPmTilesByteSource source,
        PmTilesHeader header,
        CancellationToken cancellationToken)
    {
        if (header.MetadataLength == 0)
        {
            return null;
        }

        var encoded = await source.GetBytesAsync(
            header.MetadataOffset,
            CheckedLength(header.MetadataLength, "PMTiles metadata"),
            cancellationToken).ConfigureAwait(false);
        var decoded = Decompress(encoded, header.InternalCompression, "PMTiles metadata");
        try
        {
            return JsonDocument.Parse(decoded);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("PMTiles JSON metadata is invalid.", exception);
        }
    }

    private static string? TryGetString(JsonDocument? document, string propertyName)
    {
        if (document is null ||
            document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static string GetDisplayName(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return Path.GetFileName(uri.AbsolutePath);
        }

        return Path.GetFileName(source);
    }

    private static int CheckedLength(ulong length, string description)
    {
        if (length == 0 || length > int.MaxValue)
        {
            throw new InvalidDataException($"{description} length {length} is outside the managed reader limit.");
        }

        return checked((int)length);
    }

    private static byte[] Decompress(byte[] content, PmTilesCompression compression, string description) => compression switch
    {
        PmTilesCompression.None => content,
        PmTilesCompression.Gzip => DecompressStream(content, static stream => new GZipStream(stream, CompressionMode.Decompress, leaveOpen: false)),
        PmTilesCompression.Brotli => DecompressStream(content, static stream => new BrotliStream(stream, CompressionMode.Decompress, leaveOpen: false)),
        PmTilesCompression.Zstd => throw new NotSupportedException($"{description} uses Zstandard compression, which is not supported by the managed PMTiles v3 baseline."),
        _ => throw new NotSupportedException($"{description} uses unknown PMTiles compression value {(byte)compression}."),
    };

    private static byte[] DecompressStream(byte[] content, Func<Stream, Stream> createDecompressor)
    {
        using var input = new MemoryStream(content, writable: false);
        using var decompressor = createDecompressor(input);
        using var output = new MemoryStream();
        decompressor.CopyTo(output);
        return output.ToArray();
    }

    private static TileContentType MapContentType(PmTilesTileType type) => type switch
    {
        PmTilesTileType.Mvt => TileContentType.VectorPbf,
        PmTilesTileType.Png => TileContentType.Png,
        PmTilesTileType.Jpeg => TileContentType.Jpeg,
        PmTilesTileType.WebP => TileContentType.WebP,
        _ => TileContentType.Unknown,
    };
}

internal enum PmTilesCompression : byte
{
    Unknown = 0,
    None = 1,
    Gzip = 2,
    Brotli = 3,
    Zstd = 4,
}

internal enum PmTilesTileType : byte
{
    Unknown = 0,
    Mvt = 1,
    Png = 2,
    Jpeg = 3,
    WebP = 4,
    Avif = 5,
    MapLibreVectorTile = 6,
}

internal readonly record struct PmTilesHeader(
    ulong RootDirectoryOffset,
    ulong RootDirectoryLength,
    ulong MetadataOffset,
    ulong MetadataLength,
    ulong LeafDirectoryOffset,
    ulong LeafDirectoryLength,
    ulong TileDataOffset,
    ulong TileDataLength,
    PmTilesCompression InternalCompression,
    PmTilesCompression TileCompression,
    PmTilesTileType TileType,
    int MinimumZoom,
    int MaximumZoom,
    double MinimumLongitude,
    double MinimumLatitude,
    double MaximumLongitude,
    double MaximumLatitude)
{
    public const int HeaderSize = 127;

    public static PmTilesHeader Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != HeaderSize)
        {
            throw new InvalidDataException($"PMTiles v3 header must be exactly {HeaderSize} bytes.");
        }

        if (!bytes[..7].SequenceEqual("PMTiles"u8))
        {
            throw new InvalidDataException("PMTiles magic number is missing.");
        }

        if (bytes[7] != 3)
        {
            throw new NotSupportedException($"PMTiles specification version {bytes[7]} is not supported. Expected v3.");
        }

        var minimumZoom = bytes[100];
        var maximumZoom = bytes[101];
        if (minimumZoom > maximumZoom || maximumZoom > 30)
        {
            throw new InvalidDataException($"PMTiles zoom range {minimumZoom}-{maximumZoom} is invalid for the current tile contract.");
        }

        var header = new PmTilesHeader(
            ReadUInt64(bytes, 8),
            ReadUInt64(bytes, 16),
            ReadUInt64(bytes, 24),
            ReadUInt64(bytes, 32),
            ReadUInt64(bytes, 40),
            ReadUInt64(bytes, 48),
            ReadUInt64(bytes, 56),
            ReadUInt64(bytes, 64),
            (PmTilesCompression)bytes[97],
            (PmTilesCompression)bytes[98],
            (PmTilesTileType)bytes[99],
            minimumZoom,
            maximumZoom,
            ReadInt32(bytes, 102) / 10_000_000d,
            ReadInt32(bytes, 106) / 10_000_000d,
            ReadInt32(bytes, 110) / 10_000_000d,
            ReadInt32(bytes, 114) / 10_000_000d);
        header.ValidateOffsets();
        return header;
    }

    private void ValidateOffsets()
    {
        if (RootDirectoryLength == 0 || RootDirectoryOffset < HeaderSize)
        {
            throw new InvalidDataException("PMTiles root directory offset/length is invalid.");
        }

        _ = checked(RootDirectoryOffset + RootDirectoryLength);
        _ = checked(MetadataOffset + MetadataLength);
        _ = checked(LeafDirectoryOffset + LeafDirectoryLength);
        _ = checked(TileDataOffset + TileDataLength);
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset, sizeof(ulong)));

    private static int ReadInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, sizeof(int)));
}

internal readonly record struct PmTilesDirectoryEntry(ulong TileId, ulong Offset, ulong Length, ulong RunLength);

internal static class PmTilesDirectory
{
    public static PmTilesDirectoryEntry[] Decode(ReadOnlySpan<byte> bytes)
    {
        var position = 0;
        var countValue = ReadVarInt(bytes, ref position);
        if (countValue == 0 || countValue > 1_000_000)
        {
            throw new InvalidDataException($"PMTiles directory entry count {countValue} is invalid or exceeds the managed safety limit.");
        }

        var count = checked((int)countValue);
        var entries = new PmTilesDirectoryEntry[count];
        ulong tileId = 0;
        for (var index = 0; index < count; index++)
        {
            tileId = checked(tileId + ReadVarInt(bytes, ref position));
            entries[index] = entries[index] with { TileId = tileId };
        }

        for (var index = 0; index < count; index++)
        {
            entries[index] = entries[index] with { RunLength = ReadVarInt(bytes, ref position) };
        }

        for (var index = 0; index < count; index++)
        {
            var length = ReadVarInt(bytes, ref position);
            if (length == 0)
            {
                throw new InvalidDataException("PMTiles directory entry length must be greater than zero.");
            }

            entries[index] = entries[index] with { Length = length };
        }

        ulong nextOffset = 0;
        for (var index = 0; index < count; index++)
        {
            var encodedOffset = ReadVarInt(bytes, ref position);
            var offset = encodedOffset == 0 && index > 0
                ? nextOffset
                : checked(encodedOffset - 1);
            entries[index] = entries[index] with { Offset = offset };
            nextOffset = checked(offset + entries[index].Length);
        }

        if (position != bytes.Length)
        {
            throw new InvalidDataException("PMTiles directory contains trailing bytes after the declared entries.");
        }

        return entries;
    }

    public static PmTilesDirectoryEntry? Find(PmTilesDirectoryEntry[] entries, ulong tileId)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var low = 0;
        var high = entries.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var entry = entries[middle];
            if (tileId > entry.TileId)
            {
                low = middle + 1;
            }
            else if (tileId < entry.TileId)
            {
                high = middle - 1;
            }
            else
            {
                return entry;
            }
        }

        if (high < 0)
        {
            return null;
        }

        var candidate = entries[high];
        if (candidate.RunLength == 0 || tileId - candidate.TileId < candidate.RunLength)
        {
            return candidate;
        }

        return null;
    }

    private static ulong ReadVarInt(ReadOnlySpan<byte> bytes, ref int position)
    {
        ulong result = 0;
        var shift = 0;
        while (position < bytes.Length && shift <= 63)
        {
            var current = bytes[position++];
            result |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
        }

        throw new InvalidDataException("PMTiles directory contains an incomplete or overflowing varint.");
    }
}

internal static class PmTilesTileId
{
    public static ulong FromZxy(TileCoordinate coordinate)
    {
        if (!coordinate.IsValid)
        {
            throw new ArgumentException("Tile coordinate must be valid.", nameof(coordinate));
        }

        var zoom = coordinate.Zoom;
        var x = coordinate.X;
        var y = coordinate.Y;
        var accumulated = ((1UL << (zoom * 2)) - 1UL) / 3UL;
        for (var bit = zoom - 1; bit >= 0; bit--)
        {
            var scale = 1 << bit;
            var rx = x & scale;
            var ry = y & scale;
            accumulated = checked(accumulated + ((ulong)((3 * rx) ^ ry) << bit));
            Rotate(scale, ref x, ref y, rx, ry);
        }

        return accumulated;
    }

    private static void Rotate(int scale, ref int x, ref int y, int rx, int ry)
    {
        if (ry != 0)
        {
            return;
        }

        if (rx != 0)
        {
            x = scale - 1 - x;
            y = scale - 1 - y;
        }

        (x, y) = (y, x);
    }
}

internal interface IPmTilesByteSource : IAsyncDisposable
{
    ValueTask<byte[]> GetBytesAsync(long offset, int length, CancellationToken cancellationToken);
}

internal sealed class FilePmTilesByteSource : IPmTilesByteSource
{
    private readonly FileStream _stream;

    public FilePmTilesByteSource(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.RandomAccess);
    }

    public async ValueTask<byte[]> GetBytesAsync(long offset, int length, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        if (offset > _stream.Length - length)
        {
            throw new InvalidDataException($"PMTiles byte range {offset}-{offset + length - 1} exceeds the archive length {_stream.Length}.");
        }

        var buffer = new byte[length];
        _stream.Position = offset;
        await _stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer;
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();
}

internal sealed class HttpPmTilesByteSource : IPmTilesByteSource
{
    private readonly HttpClient _httpClient;
    private readonly Uri _uri;
    private readonly TimeSpan _requestTimeout;

    public HttpPmTilesByteSource(HttpClient httpClient, Uri uri, TimeSpan requestTimeout)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(requestTimeout, TimeSpan.Zero);
        _httpClient = httpClient;
        _uri = uri;
        _requestTimeout = requestTimeout;
    }

    public async ValueTask<byte[]> GetBytesAsync(long offset, int length, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_requestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, _uri);
        request.Headers.Range = new RangeHeaderValue(offset, checked(offset + length - 1L));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SpatialViewer.GisCore", "0.4"));
        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                throw new NotSupportedException(
                    $"Remote PMTiles server must support HTTP Range requests. Expected 206 Partial Content, received {(int)response.StatusCode} {response.StatusCode}.");
            }

            var range = response.Content.Headers.ContentRange;
            if (range?.From != offset || range.To != offset + length - 1L)
            {
                throw new InvalidDataException("Remote PMTiles response returned an invalid Content-Range header.");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(timeoutSource.Token).ConfigureAwait(false);
            if (bytes.Length != length)
            {
                throw new InvalidDataException($"Remote PMTiles range returned {bytes.Length} bytes; expected {length}.");
            }

            return bytes;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException($"Remote PMTiles range request for '{_uri}' exceeded {_requestTimeout}.");
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
