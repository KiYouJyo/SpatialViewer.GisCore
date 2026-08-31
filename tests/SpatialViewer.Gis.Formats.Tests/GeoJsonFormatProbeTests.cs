using SpatialViewer.Formats.Gis.GeoJson;
using Xunit;

namespace SpatialViewer.Gis.Formats.Tests;

public sealed class GeoJsonFormatProbeTests
{
    [Theory]
    [InlineData("sample.geojson", true)]
    [InlineData("sample.GEOJSON", true)]
    [InlineData("sample.json", true)]
    [InlineData("sample.shp", false)]
    public async Task ProbeUsesExpectedExtensions(string path, bool expected)
    {
        var probe = new GeoJsonFormatProbe();
        var result = await probe.ProbeAsync(path);

        Assert.Equal(expected, result.IsMatch);
    }
}
