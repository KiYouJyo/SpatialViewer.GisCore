using System.Buffers.Binary;
using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis.Shapefile;

internal readonly record struct ShapefileHeader(
    int ShapeType,
    Envelope2D Bounds,
    double? MinZ,
    double? MaxZ,
    double? MinM,
    double? MaxM);

internal readonly record struct ShapefileRecordIndexEntry(
    int RecordNumber,
    long OffsetBytes,
    int ContentLengthBytes);

internal static class ShapefileBinary
{
    public const int HeaderLength = 100;
    public const int FileCode = 9994;
    public const int Version = 1000;

    public static async ValueTask<ShapefileHeader> ReadHeaderAsync(
        FileStream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[HeaderLength];
        stream.Position = 0;
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);

        var fileCode = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0, 4));
        if (fileCode != FileCode)
        {
            throw new InvalidDataException($"Invalid shapefile file code {fileCode}; expected {FileCode}.");
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(28, 4));
        if (version != Version)
        {
            throw new InvalidDataException($"Unsupported shapefile version {version}; expected {Version}.");
        }

        var shapeType = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(32, 4));
        var bounds = new Envelope2D(
            ReadDoubleLittleEndian(buffer, 36),
            ReadDoubleLittleEndian(buffer, 44),
            ReadDoubleLittleEndian(buffer, 52),
            ReadDoubleLittleEndian(buffer, 60));
        var minZ = NormalizeRangeValue(ReadDoubleLittleEndian(buffer, 68));
        var maxZ = NormalizeRangeValue(ReadDoubleLittleEndian(buffer, 76));
        var minM = NormalizeMeasure(ReadDoubleLittleEndian(buffer, 84));
        var maxM = NormalizeMeasure(ReadDoubleLittleEndian(buffer, 92));

        return new ShapefileHeader(shapeType, bounds, minZ, maxZ, minM, maxM);
    }

    public static async ValueTask<List<ShapefileRecordIndexEntry>> ReadIndexAsync(
        string indexPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            indexPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        _ = await ReadHeaderAsync(stream, cancellationToken).ConfigureAwait(false);

        if ((stream.Length - HeaderLength) % 8 != 0)
        {
            throw new InvalidDataException("The SHX index length is not aligned to 8-byte records.");
        }

        var count = checked((int)((stream.Length - HeaderLength) / 8));
        var result = new List<ShapefileRecordIndexEntry>(count);
        var recordBuffer = new byte[8];

        for (var index = 0; index < count; index++)
        {
            await stream.ReadExactlyAsync(recordBuffer, cancellationToken).ConfigureAwait(false);
            var offsetWords = BinaryPrimitives.ReadInt32BigEndian(recordBuffer.AsSpan(0, 4));
            var contentLengthWords = BinaryPrimitives.ReadInt32BigEndian(recordBuffer.AsSpan(4, 4));

            if (offsetWords < HeaderLength / 2 || contentLengthWords < 2)
            {
                throw new InvalidDataException($"Invalid SHX entry at index {index}.");
            }

            result.Add(new ShapefileRecordIndexEntry(
                index + 1,
                checked((long)offsetWords * 2L),
                checked(contentLengthWords * 2)));
        }

        return result;
    }

    public static double ReadDoubleLittleEndian(ReadOnlySpan<byte> buffer, int offset)
    {
        var bits = BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(offset, sizeof(long)));
        return BitConverter.Int64BitsToDouble(bits);
    }

    public static int ReadInt32LittleEndian(ReadOnlySpan<byte> buffer, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset, sizeof(int)));

    public static double? NormalizeMeasure(double value) =>
        value <= -1.0e38 || double.IsNaN(value) || double.IsInfinity(value)
            ? null
            : value;

    private static double? NormalizeRangeValue(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? null : value;
}
