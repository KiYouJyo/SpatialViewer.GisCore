using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using SpatialViewer.Formats.Gis;
using SpatialViewer.Gis.Core;
using SpatialViewer.Gis.Projections;

namespace SpatialViewer.Formats.Gis.Shapefile;

public sealed class ShapefileDataSourceReader : IGisDataSourceReader
{
    public string FormatId => ShapefileFormatProbe.FormatId;

    public async ValueTask<GisDatasetMetadata> ReadMetadataAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var files = ValidateDataset(path);

        await using var shapeStream = OpenShapeStream(files.ShapePath);
        var header = await ShapefileBinary.ReadHeaderAsync(shapeStream, cancellationToken).ConfigureAwait(false);
        var index = await ShapefileBinary.ReadIndexAsync(files.IndexPath, cancellationToken).ConfigureAwait(false);
        await using var dbf = await DbfTableReader.OpenAsync(
            files.AttributePath,
            files.CodePagePath,
            cancellationToken).ConfigureAwait(false);

        if (dbf.RecordCount != index.Count)
        {
            throw new InvalidDataException(
                $"Shapefile sidecars disagree on record count: SHX={index.Count}, DBF={dbf.RecordCount}.");
        }

        var spatialReference = await ReadSpatialReferenceAsync(files.ProjectionPath, cancellationToken)
            .ConfigureAwait(false);
        var layerName = Path.GetFileNameWithoutExtension(files.ShapePath);
        var layer = new VectorLayerMetadata(
            layerName,
            spatialReference,
            header.Bounds.IsValid ? header.Bounds : null,
            ShapefileGeometryReader.MapGeometryType(header.ShapeType),
            index.Count);

        return new GisDatasetMetadata(
            Path.GetFileName(files.ShapePath),
            FormatId,
            new GisLayerMetadata[] { layer });
    }

    public async IAsyncEnumerable<GisFeature> ReadFeaturesAsync(
        string path,
        string layerName,
        Envelope2D? extent = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
        var files = ValidateDataset(path);
        var expectedLayerName = Path.GetFileNameWithoutExtension(files.ShapePath);

        if (!string.Equals(layerName, expectedLayerName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Shapefile layer '{layerName}' was not found. Expected '{expectedLayerName}'.",
                nameof(layerName));
        }

        var index = await ShapefileBinary.ReadIndexAsync(files.IndexPath, cancellationToken).ConfigureAwait(false);
        await using var shapeStream = OpenShapeStream(files.ShapePath);
        _ = await ShapefileBinary.ReadHeaderAsync(shapeStream, cancellationToken).ConfigureAwait(false);
        await using var dbf = await DbfTableReader.OpenAsync(
            files.AttributePath,
            files.CodePagePath,
            cancellationToken).ConfigureAwait(false);

        if (dbf.RecordCount != index.Count)
        {
            throw new InvalidDataException(
                $"Shapefile sidecars disagree on record count: SHX={index.Count}, DBF={dbf.RecordCount}.");
        }

        for (var zeroBasedIndex = 0; zeroBasedIndex < index.Count; zeroBasedIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var indexEntry = index[zeroBasedIndex];
            var content = await ReadRecordContentAsync(shapeStream, indexEntry, cancellationToken)
                .ConfigureAwait(false);
            var geometry = ShapefileGeometryReader.Parse(content);

            if (extent is not null &&
                (geometry?.Bounds is null || !geometry.Bounds.Value.Intersects(extent.Value)))
            {
                continue;
            }

            var dbfRecord = await dbf.ReadRecordAsync(zeroBasedIndex, cancellationToken).ConfigureAwait(false);
            if (dbfRecord.IsDeleted)
            {
                continue;
            }

            yield return new GisFeature(
                indexEntry.RecordNumber.ToString(CultureInfo.InvariantCulture),
                geometry,
                dbfRecord.Attributes,
                geometry?.DeclaredBounds);
        }
    }

    private static FileStream OpenShapeStream(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        64 * 1024,
        FileOptions.Asynchronous | FileOptions.RandomAccess);

    private static async ValueTask<byte[]> ReadRecordContentAsync(
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

        var content = new byte[contentLengthBytes];
        await stream.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
        return content;
    }

    private static async ValueTask<SpatialReference> ReadSpatialReferenceAsync(
        string projectionPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(projectionPath))
        {
            return SpatialReference.Unknown;
        }

        var wkt = (await File.ReadAllTextAsync(projectionPath, cancellationToken).ConfigureAwait(false)).Trim();
        if (wkt.Length == 0)
        {
            throw new InvalidDataException("The PRJ sidecar exists but is empty.");
        }

        return SpatialReferenceParser.ParseWkt(wkt);
    }

    private static ShapefileDatasetFiles ValidateDataset(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var shapePath = Path.GetFullPath(path);
        if (!Path.GetExtension(shapePath).Equals(".shp", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Shapefile reader requires a .shp path.", nameof(path));
        }

        var indexPath = Path.ChangeExtension(shapePath, ".shx");
        var attributePath = Path.ChangeExtension(shapePath, ".dbf");
        var projectionPath = Path.ChangeExtension(shapePath, ".prj");
        var codePagePath = Path.ChangeExtension(shapePath, ".cpg");

        RequireFile(shapePath, "SHP geometry");
        RequireFile(indexPath, "SHX index");
        RequireFile(attributePath, "DBF attributes");

        return new ShapefileDatasetFiles(
            shapePath,
            indexPath,
            attributePath,
            projectionPath,
            codePagePath);
    }

    private static void RequireFile(string path, string description)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required shapefile sidecar is missing: {description} '{path}'.", path);
        }
    }

    private sealed record ShapefileDatasetFiles(
        string ShapePath,
        string IndexPath,
        string AttributePath,
        string ProjectionPath,
        string CodePagePath);
}
