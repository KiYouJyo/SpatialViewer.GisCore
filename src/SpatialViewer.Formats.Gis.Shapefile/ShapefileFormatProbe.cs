using SpatialViewer.Formats.Gis;

namespace SpatialViewer.Formats.Gis.Shapefile;

public sealed class ShapefileFormatProbe : IGisFormatProbe
{
    public const string FormatId = "shapefile";

    public ValueTask<GisFormatProbeResult> ProbeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var isMatch = Path.GetExtension(path).Equals(".shp", StringComparison.OrdinalIgnoreCase);
        return ValueTask.FromResult(isMatch
            ? new GisFormatProbeResult(true, FormatId, 100)
            : GisFormatProbeResult.NoMatch);
    }
}
