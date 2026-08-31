using SpatialViewer.Formats.Gis.GeoTiff;
using SpatialViewer.Gis.Core;
using Xunit;

namespace SpatialViewer.Gis.Formats.Tests;

public sealed class GeoTiffReaderTests
{
    private readonly GeoTiffDataSourceReader _reader = new();

    [Theory]
    [InlineData("sample.tif", true)]
    [InlineData("sample.TIFF", true)]
    [InlineData("sample.png", false)]
    public async Task ProbeUsesTiffExtensions(string path, bool expected)
    {
        var result = await new GeoTiffFormatProbe().ProbeAsync(path);
        Assert.Equal(expected, result.IsMatch);
    }

    [Fact]
    public async Task ReadsGeoreferencingBandsNoDataAndOverviewMetadata()
    {
        var metadata = await _reader.ReadMetadataAsync(GetFixturePath("phase3-tiled-overview.tif"));
        var layer = Assert.IsType<RasterLayerMetadata>(Assert.Single(metadata.Layers));

        Assert.Equal("geotiff", metadata.SourceKind);
        Assert.Equal(32, layer.Width);
        Assert.Equal(32, layer.Height);
        Assert.Equal(3, layer.BandCount);
        Assert.True(layer.IsTiled);
        Assert.Equal("RGB", layer.ColorModel);
        Assert.Equal(new SpatialReference("EPSG", "3857"), layer.SpatialReference);
        Assert.Equal(new RasterGeoTransform(100, 10, 0, 200, 0, -10), layer.GeoTransform);
        Assert.Equal(new Envelope2D(100, -120, 420, 200), layer.Bounds);
        Assert.Equal(0d, layer.Bands[0].NoDataValue);

        var overview = Assert.Single(layer.Overviews);
        Assert.Equal(1, overview.Level);
        Assert.Equal(16, overview.Width);
        Assert.Equal(16, overview.Height);
        Assert.Equal(2d, overview.DecimationX);
        Assert.Equal(2d, overview.DecimationY);
    }

    [Fact]
    public async Task ReadsOnlyRequestedTiledWindowIntoTopLeftRgbaOrder()
    {
        var result = await _reader.ReadRasterAsync(
            GetFixturePath("phase3-tiled-overview.tif"),
            "raster",
            new RasterReadRequest(new RasterWindow(4, 6, 8, 10), 8, 10));

        Assert.Equal(0, result.OverviewLevel);
        Assert.Equal(new RasterWindow(4, 6, 8, 10), result.SourceWindow);
        AssertPixel(result, 0, 0, 4, 6, 10, 255);
        AssertPixel(result, 7, 9, 11, 15, 26, 255);
    }

    [Fact]
    public async Task ReadsOnlyIntersectingStripsInTopLeftOrder()
    {
        var result = await _reader.ReadRasterAsync(
            GetFixturePath("phase3-strip.tif"),
            "raster",
            new RasterReadRequest(new RasterWindow(2, 3, 4, 3), 4, 3));

        Assert.Equal(0, result.OverviewLevel);
        AssertPixel(result, 0, 0, 20, 60, 40, 255);
        AssertPixel(result, 3, 2, 50, 100, 80, 255);
    }

    [Fact]
    public async Task SelectsInternalOverviewForDownsampledViewport()
    {
        var result = await _reader.ReadRasterAsync(
            GetFixturePath("phase3-tiled-overview.tif"),
            "raster",
            new RasterReadRequest(new RasterWindow(0, 0, 32, 32), 8, 8));

        Assert.Equal(1, result.OverviewLevel);
        Assert.Equal(8, result.Width);
        Assert.Equal(8, result.Height);
        AssertPixel(result, 0, 0, 2, 2, 4, 255);
    }

    private static string GetFixturePath(string fileName) => Path.Combine(
        AppContext.BaseDirectory,
        "fixtures",
        "geotiff",
        fileName);

    private static void AssertPixel(
        RasterReadResult result,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        var pixels = result.Pixels.Span;
        var offset = ((y * result.Width) + x) * 4;
        Assert.Equal(red, pixels[offset]);
        Assert.Equal(green, pixels[offset + 1]);
        Assert.Equal(blue, pixels[offset + 2]);
        Assert.Equal(alpha, pixels[offset + 3]);
    }
}
