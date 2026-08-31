using SpatialViewer.Formats.Gis.Shapefile;
using SpatialViewer.Gis.Core;
using Xunit;

namespace SpatialViewer.Gis.Formats.Tests;

public sealed class ShapefileReaderTests
{
    private readonly ShapefileDataSourceReader _reader = new();

    [Theory]
    [InlineData("sample.shp", true)]
    [InlineData("sample.SHP", true)]
    [InlineData("sample.shx", false)]
    [InlineData("sample.geojson", false)]
    public async Task ProbeUsesShpExtension(string path, bool expected)
    {
        var probe = new ShapefileFormatProbe();
        var result = await probe.ProbeAsync(path);

        Assert.Equal(expected, result.IsMatch);
    }

    [Fact]
    public async Task ReadsPointZmDbfCpgAndPrjWithoutCoordinateLoss()
    {
        var path = Fixture("points-zm.shp");
        var metadata = await _reader.ReadMetadataAsync(path);
        var layer = Assert.IsType<VectorLayerMetadata>(Assert.Single(metadata.Layers));

        Assert.Equal("shapefile", metadata.SourceKind);
        Assert.Equal(2L, layer.FeatureCount);
        Assert.Equal(GisGeometryType.Point, layer.GeometryType);
        Assert.Equal(new Envelope2D(1, 2, 100, 100), layer.Bounds);
        Assert.Equal("EPSG", layer.SpatialReference.Authority);
        Assert.Equal("4326", layer.SpatialReference.Code);

        var features = await ReadAllAsync(path, layer.Name);
        Assert.Equal(2, features.Count);

        var first = features[0];
        Assert.Equal("1", first.Id);
        var point = Assert.IsType<PointGeometry>(first.Geometry);
        Assert.Equal(new GisCoordinate(1, 2, 3, 4), point.Coordinate);
        Assert.Equal("東京", Assert.IsType<string>(first.Attributes["NAME"]));
        Assert.Equal(12.50m, Assert.IsType<decimal>(first.Attributes["VALUE"]));
        Assert.True(Assert.IsType<bool>(first.Attributes["ACTIVE"]));
        Assert.Equal(new DateOnly(2026, 8, 31), Assert.IsType<DateOnly>(first.Attributes["WHEN"]));

        var second = Assert.IsType<PointGeometry>(features[1].Geometry);
        Assert.Equal(new GisCoordinate(100, 100, 5, 6), second.Coordinate);
    }

    [Fact]
    public async Task ExtentFilterSkipsOutsideShapefileRecords()
    {
        var path = Fixture("points-zm.shp");
        var metadata = await _reader.ReadMetadataAsync(path);
        var layer = Assert.IsType<VectorLayerMetadata>(Assert.Single(metadata.Layers));

        var features = await ReadAllAsync(path, layer.Name, new Envelope2D(0, 0, 10, 10));

        var feature = Assert.Single(features);
        Assert.Equal("1", feature.Id);
        Assert.Equal("東京", feature.Attributes["NAME"]);
    }

    [Fact]
    public async Task ReadsMultipartPolylineWithPerVertexZm()
    {
        var path = Fixture("polyline-zm.shp");
        var metadata = await _reader.ReadMetadataAsync(path);
        var layer = Assert.IsType<VectorLayerMetadata>(Assert.Single(metadata.Layers));

        Assert.Equal(GisGeometryType.MultiLineString, layer.GeometryType);
        var feature = Assert.Single(await ReadAllAsync(path, layer.Name));
        var geometry = Assert.IsType<MultiLineStringGeometry>(feature.Geometry);

        Assert.Equal(2, geometry.Lines.Count);
        Assert.Equal(new GisCoordinate(0, 0, 1, 11), geometry.Lines[0][0]);
        Assert.Equal(new GisCoordinate(11, 11, 4, 14), geometry.Lines[1][1]);
        Assert.Equal("route", feature.Attributes["NAME"]);
    }

    [Fact]
    public async Task MissingPrjRemainsUnknownInsteadOfAssumingWgs84()
    {
        var tempDirectory = CreateTemporaryDirectory();
        var shapePath = Path.Combine(tempDirectory, "points.shp");

        try
        {
            CopyFixtureSidecar("points-zm", ".shp", shapePath);
            CopyFixtureSidecar("points-zm", ".shx", Path.ChangeExtension(shapePath, ".shx"));
            CopyFixtureSidecar("points-zm", ".dbf", Path.ChangeExtension(shapePath, ".dbf"));
            CopyFixtureSidecar("points-zm", ".cpg", Path.ChangeExtension(shapePath, ".cpg"));

            var metadata = await _reader.ReadMetadataAsync(shapePath);
            var layer = Assert.IsType<VectorLayerMetadata>(Assert.Single(metadata.Layers));

            Assert.True(layer.SpatialReference.IsUnknown);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task MissingRequiredSidecarHasActionableDiagnostic()
    {
        var tempDirectory = CreateTemporaryDirectory();
        var shapePath = Path.Combine(tempDirectory, "broken.shp");

        try
        {
            File.Copy(Fixture("points-zm.shp"), shapePath);

            var exception = await Assert.ThrowsAsync<FileNotFoundException>(
                async () => await _reader.ReadMetadataAsync(shapePath));

            Assert.Contains("SHX index", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private async Task<List<GisFeature>> ReadAllAsync(
        string path,
        string layerName,
        Envelope2D? extent = null)
    {
        var result = new List<GisFeature>();
        await foreach (var feature in _reader.ReadFeaturesAsync(path, layerName, extent))
        {
            result.Add(feature);
        }

        return result;
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "shapefile", name);

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SpatialViewer-GisCore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CopyFixtureSidecar(string baseName, string extension, string destination) =>
        File.Copy(Fixture(baseName + extension), destination);
}
