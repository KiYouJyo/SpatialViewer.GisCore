using SpatialViewer.Formats.Gis;

namespace SpatialViewer.Formats.Gis.GeoTiff;

public sealed class GeoTiffFormatProbe : IGisFormatProbe
{
    public ValueTask<GisFormatProbeResult> ProbeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var extension = Path.GetExtension(path);
        var isMatch = extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
        return ValueTask.FromResult(
            isMatch
                ? new GisFormatProbeResult(true, "geotiff", 90)
                : GisFormatProbeResult.NoMatch);
    }
}
