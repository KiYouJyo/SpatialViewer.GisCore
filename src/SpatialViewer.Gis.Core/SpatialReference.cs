namespace SpatialViewer.Gis.Core;

public sealed record SpatialReference(
    string? Authority,
    string? Code,
    string? WellKnownText = null,
    string? Name = null)
{
    public static SpatialReference Unknown { get; } = new(null, null);

    public bool IsUnknown => string.IsNullOrWhiteSpace(Authority) && string.IsNullOrWhiteSpace(Code) && string.IsNullOrWhiteSpace(WellKnownText);

    public static SpatialReference FromEpsg(int epsg) => new("EPSG", epsg.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
