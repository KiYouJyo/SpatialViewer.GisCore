using SpatialViewer.Formats.Gis;

namespace SpatialViewer.Formats.Gis.MbTiles;

public sealed class MbTilesFormatProbe : IGisFormatProbe
{
    public const string FormatId = "mbtiles";

    public ValueTask<GisFormatProbeResult> ProbeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var matches = string.Equals(Path.GetExtension(path), ".mbtiles", StringComparison.OrdinalIgnoreCase);
        return ValueTask.FromResult(matches
            ? new GisFormatProbeResult(true, FormatId, 100)
            : GisFormatProbeResult.NoMatch);
    }
}
