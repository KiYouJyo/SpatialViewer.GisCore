using System.Buffers.Binary;
using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis.Shapefile;

internal static class ShapefileSpatialIndex
{
    private const int MaximumBoundsPrefixLength = 36;

    public static async ValueTask<int[]> FindCandidatesAsync(
        FileStream shapeStream,
        List<ShapefileRecordIndexEntry> index,
        Envelope2D extent,
        CancellationToken cancellationToken)
    {
        var entries = new List<SpatialIndexEntry<int>>(index.Count);

        for (var zeroBasedIndex = 0; zeroBasedIndex < index.Count; zeroBasedIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bounds = await ReadRecordBoundsAsync(
                shapeStream,
                index[zeroBasedIndex],
                cancellationToken).ConfigureAwait(false);

            if (bounds is { IsValid: true })
            {
                entries.Add(new SpatialIndexEntry<int>(bounds.Value, zeroBasedIndex));
            }
        }

        var tree = new PackedRTree<int>(entries);
        var matches = tree.Query(extent);
        var result = new int[matches.Count];

        for (var indexPosition = 0; indexPosition < matches.Count; indexPosition++)
        {
            result[indexPosition] = matches[indexPosition];
        }

        Array.Sort(result);
        return result;
    }

    private static async ValueTask<Envelope2D?> ReadRecordBoundsAsync(
        FileStream stream,
        ShapefileRecordIndexEntry indexEntry,
        CancellationToken cancellationToken)
    {
        stream.Position = indexEntry.OffsetBytes;
        var recordHeader = new byte[8];
        await stream.ReadExactlyAsync(recordHeader, cancellationToken).ConfigureAwait(false);

        var recordNumber = BinaryPrimitives.ReadInt32BigEndian(recordHeader.AsSpan(0, 4));
        var contentLengthBytes = checked(BinaryPrimitives.ReadInt32BigEndian(recordHeader.AsSpan(4, 4)) * 2);

        if (recordNumber != indexEntry.RecordNumber)
        {
            throw new InvalidDataException(
                $"SHP record number {recordNumber} does not match SHX record {indexEntry.RecordNumber}.");
        }

        if (contentLengthBytes != indexEntry.ContentLengthBytes)
        {
            throw new InvalidDataException(
                $"SHP record {recordNumber} length {contentLengthBytes} does not match SHX length {indexEntry.ContentLengthBytes}.");
        }

        var prefixLength = Math.Min(contentLengthBytes, MaximumBoundsPrefixLength);
        var prefix = new byte[prefixLength];
        await stream.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
        return ShapefileBoundsReader.ReadBounds(prefix);
    }
}
