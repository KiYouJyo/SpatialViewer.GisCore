using SpatialViewer.Formats.Gis.WorldImage;
using SpatialViewer.Gis.Core;
using Xunit;

namespace SpatialViewer.Gis.Formats.Tests;

public sealed class WorldImageReaderTests
{
    private readonly WorldImageDataSourceReader _reader = new();

    [Theory]
    [InlineData("sample.png", true)]
    [InlineData("sample.JPG", true)]
    [InlineData("sample.jpeg", true)]
    [InlineData("sample.tif", false)]
    public async Task ProbeUsesImageExtensions(string path, bool expected)
    {
        var result = await new WorldImageFormatProbe().ProbeAsync(path);
        Assert.Equal(expected, result.IsMatch);
    }

    [Fact]
    public async Task ReadsPngWorldFilePrjAndRotatedBounds()
    {
        var metadata = await _reader.ReadMetadataAsync(GetFixturePath("rotated.png"));
        var layer = Assert.IsType<RasterLayerMetadata>(Assert.Single(metadata.Layers));
        var expectedTransform = new RasterGeoTransform(100, 2, 0.25, 200, 0.5, -3);

        Assert.Equal("world-image", metadata.SourceKind);
        Assert.Equal(12, layer.Width);
        Assert.Equal(10, layer.Height);
        Assert.Equal(4, layer.BandCount);
        Assert.Equal(expectedTransform, layer.GeoTransform);
        Assert.Equal(expectedTransform.GetBounds(12, 10), layer.Bounds);
        Assert.Equal("EPSG", layer.SpatialReference.Authority);
        Assert.Equal("4326", layer.SpatialReference.Code);
        Assert.Equal("RGBA", layer.ColorModel);
        Assert.False(layer.IsTiled);
    }

    [Fact]
    public async Task ReadsExactPngWindowInTopLeftOrder()
    {
        var result = await _reader.ReadRasterAsync(
            GetFixturePath("rotated.png"),
            "raster",
            new RasterReadRequest(new RasterWindow(2, 3, 4, 3), 4, 3));

        Assert.Equal(0, result.OverviewLevel);
        Assert.Equal(new RasterWindow(2, 3, 4, 3), result.SourceWindow);
        AssertPixel(result, 0, 0, 20, 60, 40, 255);
        AssertPixel(result, 3, 2, 50, 100, 80, 255);
    }

    [Fact]
    public async Task ReadsJpegMetadataWithJgwAndPrj()
    {
        var metadata = await _reader.ReadMetadataAsync(GetFixturePath("photo.jpg"));
        var layer = Assert.IsType<RasterLayerMetadata>(Assert.Single(metadata.Layers));

        Assert.Equal(12, layer.Width);
        Assert.Equal(10, layer.Height);
        Assert.NotNull(layer.GeoTransform);
        Assert.Equal("4326", layer.SpatialReference.Code);
        Assert.Equal("RGB", layer.ColorModel);
    }

    private static string GetFixturePath(string fileName) => Path.Combine(
        AppContext.BaseDirectory,
        "fixtures",
        "world-image",
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
