using SpatialViewer.Formats.Gis;

namespace SpatialViewer.Formats.Gis.GeoPackage;

public sealed class GeoPackageFormatProbe : IGisFormatProbe
{
    public const string FormatId = "geopackage";

    public ValueTask<GisFormatProbeResult> ProbeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var isMatch = Path.GetExtension(path).Equals(".gpkg", StringComparison.OrdinalIgnoreCase);
        return ValueTask.FromResult(isMatch
            ? new GisFormatProbeResult(true, FormatId, 100)
            : GisFormatProbeResult.NoMatch);
    }
}
