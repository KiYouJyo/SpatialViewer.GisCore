using SpatialViewer.Formats.Gis;

namespace SpatialViewer.Formats.Gis.WorldImage;

public sealed class WorldImageFormatProbe : IGisFormatProbe
{
    public ValueTask<GisFormatProbeResult> ProbeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var extension = Path.GetExtension(path);
        var isMatch = extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
        return ValueTask.FromResult(
            isMatch
                ? new GisFormatProbeResult(true, "world-image", 70)
                : GisFormatProbeResult.NoMatch);
    }
}
