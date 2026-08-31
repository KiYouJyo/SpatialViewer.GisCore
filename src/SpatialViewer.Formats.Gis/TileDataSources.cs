using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis;

public interface ITileDataSourceReader
{
    string FormatId { get; }

    ValueTask<TileSourceMetadata> ReadMetadataAsync(
        string source,
        CancellationToken cancellationToken = default);

    ValueTask<TileReadResult?> ReadTileAsync(
        string source,
        string layerName,
        TileCoordinate coordinate,
        CancellationToken cancellationToken = default);
}
