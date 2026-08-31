using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis;

public readonly record struct GisFormatProbeResult(bool IsMatch, string? FormatId = null, int Confidence = 0)
{
    public static GisFormatProbeResult NoMatch => new(false);
}

public interface IGisFormatProbe
{
    ValueTask<GisFormatProbeResult> ProbeAsync(string path, CancellationToken cancellationToken = default);
}

public interface IGisDataSourceReader
{
    string FormatId { get; }

    ValueTask<GisDatasetMetadata> ReadMetadataAsync(string path, CancellationToken cancellationToken = default);

    IAsyncEnumerable<GisFeature> ReadFeaturesAsync(
        string path,
        string layerName,
        Envelope2D? extent = null,
        CancellationToken cancellationToken = default);
}
