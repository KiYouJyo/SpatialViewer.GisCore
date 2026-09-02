using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis;

public interface IMapImageDataSourceReader
{
    string FormatId { get; }

    ValueTask<MapImageResult> ReadMapAsync(
        string source,
        string layerName,
        MapImageRequest request,
        CancellationToken cancellationToken = default);
}
