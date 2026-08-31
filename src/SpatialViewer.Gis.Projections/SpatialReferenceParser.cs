using System.Globalization;
using System.Text.RegularExpressions;
using SpatialViewer.Gis.Core;

namespace SpatialViewer.Gis.Projections;

public static partial class SpatialReferenceParser
{
    public static SpatialReference ParseWkt(string wellKnownText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wellKnownText);

        var nameMatch = RootNameRegex().Match(wellKnownText);
        var name = nameMatch.Success ? nameMatch.Groups["name"].Value : null;

        string? authority = null;
        string? code = null;

        foreach (Match match in AuthorityRegex().Matches(wellKnownText))
        {
            if (!match.Success)
            {
                continue;
            }

            authority = match.Groups["authority"].Value;
            code = match.Groups["code"].Value;
        }

        return new SpatialReference(authority, code, wellKnownText, name);
    }

    public static bool TryGetEpsg(SpatialReference spatialReference, out int epsg)
    {
        ArgumentNullException.ThrowIfNull(spatialReference);

        if (string.Equals(spatialReference.Authority, "EPSG", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(spatialReference.Code, NumberStyles.None, CultureInfo.InvariantCulture, out epsg))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(spatialReference.WellKnownText))
        {
            var parsed = ParseWkt(spatialReference.WellKnownText);
            if (string.Equals(parsed.Authority, "EPSG", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(parsed.Code, NumberStyles.None, CultureInfo.InvariantCulture, out epsg))
            {
                return true;
            }
        }

        epsg = default;
        return false;
    }

    [GeneratedRegex("^[A-Za-z0-9_]+\\s*\\[\\s*\\\"(?<name>[^\\\"]+)\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex RootNameRegex();

    [GeneratedRegex("(?:AUTHORITY|ID)\\s*\\[\\s*\\\"(?<authority>[^\\\"]+)\\\"\\s*,\\s*\\\"?(?<code>[0-9]+)\\\"?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorityRegex();
}
