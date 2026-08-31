namespace SpatialViewer.Formats.Gis.GeoJson;

public sealed class GeoJsonFormatProbe : IGisFormatProbe
{
    public const string FormatId = "geojson";

    public ValueTask<GisFormatProbeResult> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(path);
        var isMatch = extension.Equals(".geojson", StringComparison.OrdinalIgnoreCase) ||
                      extension.Equals(".json", StringComparison.OrdinalIgnoreCase);

        return ValueTask.FromResult(isMatch
            ? new GisFormatProbeResult(true, FormatId, extension.Equals(".geojson", StringComparison.OrdinalIgnoreCase) ? 100 : 50)
            : GisFormatProbeResult.NoMatch);
    }
}
