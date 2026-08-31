using System.Text.Json;
using SpatialViewer.Formats.Gis.GeoJson;
using SpatialViewer.Gis.Core;
using SpatialViewer.Gis.Rendering;
using Xunit;

namespace SpatialViewer.Gis.Formats.Tests;

public sealed class GeoJsonReaderTests
{
    private readonly GeoJsonDataSourceReader _reader = new();

    [Fact]
    public async Task ReadsAllGeometryTypesAndKeepsMissingCrsUnknown()
    {
        var path = Fixture("all-geometries.geojson");
        var metadata = await _reader.ReadMetadataAsync(path).ConfigureAwait(false);
        var layer = Assert.IsType<VectorLayerMetadata>(Assert.Single(metadata.Layers));

        Assert.Equal("geojson", metadata.SourceKind);
        Assert.Equal(8L, layer.FeatureCount);
        Assert.Null(layer.GeometryType);
        Assert.True(layer.SpatialReference.IsUnknown);
        Assert.Equal(new Envelope2D(-10, -10, 30, 30), layer.Bounds);

        var features = await ReadAllAsync(path, layer.Name).ConfigureAwait(false);
        Assert.Collection(
            features.Take(7),
            feature => Assert.IsType<PointGeometry>(feature.Geometry),
            feature => Assert.IsType<MultiPointGeometry>(feature.Geometry),
            feature => Assert.IsType<LineStringGeometry>(feature.Geometry),
            feature => Assert.IsType<MultiLineStringGeometry>(feature.Geometry),
            feature => Assert.IsType<PolygonGeometry>(feature.Geometry),
            feature => Assert.IsType<MultiPolygonGeometry>(feature.Geometry),
            feature => Assert.IsType<GeometryCollectionGeometry>(feature.Geometry));

        Assert.Null(features[7].Geometry);
        Assert.Empty(features[7].Attributes);
    }

    [Fact]
    public async Task PreservesFeatureIdAttributesBboxAndZ()
    {
        var path = Fixture("all-geometries.geojson");
        var metadata = await _reader.ReadMetadataAsync(path).ConfigureAwait(false);
        var layer = Assert.IsType<VectorLayerMetadata>(Assert.Single(metadata.Layers));
        var feature = (await ReadAllAsync(path, layer.Name).ConfigureAwait(false))[0];

        Assert.Equal("42", feature.Id);
        Assert.Equal(new GisBoundingBox(new Envelope2D(1, 2, 1, 2)), feature.DeclaredBounds);

        var point = Assert.IsType<PointGeometry>(feature.Geometry);
        Assert.Equal(new GisCoordinate(1, 2, 3), point.Coordinate);

        Assert.Equal("point", Assert.IsType<string>(feature.Attributes["name"]));
        Assert.True(Assert.IsType<bool>(feature.Attributes["active"]));
        Assert.Equal(2L, Assert.IsType<long>(feature.Attributes["count"]));
        Assert.Equal(1.5m, Assert.IsType<decimal>(feature.Attributes["ratio"]));
        Assert.Null(feature.Attributes["nullable"]);

        var tags = Assert.IsAssignableFrom<IReadOnlyList<object?>>(feature.Attributes["tags"]);
        Assert.Equal("a", Assert.IsType<string>(tags[0]));
        Assert.Equal(3L, Assert.IsType<long>(tags[1]));

        var nested = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(feature.Attributes["meta"]);
        Assert.Equal("A", Assert.IsType<string>(nested["zone"]));
    }

    [Fact]
    public async Task ExtentFilterUsesActualFeatureBounds()
    {
        var path = Fixture("all-geometries.geojson");
        var metadata = await _reader.ReadMetadataAsync(path).ConfigureAwait(false);
        var layer = Assert.IsType<VectorLayerMetadata>(Assert.Single(metadata.Layers));

        var features = await ReadAllAsync(
            path,
            layer.Name,
            new Envelope2D(0.5, 0.5, 2.5, 2.5)).ConfigureAwait(false);

        Assert.Contains(features, feature => feature.Id == "42");
        Assert.Contains(features, feature => feature.Id == "mp");
        Assert.Contains(features, feature => feature.Id == "line");
        Assert.Contains(features, feature => feature.Id == "poly");
        Assert.DoesNotContain(features, feature => feature.Id == "ml");
        Assert.DoesNotContain(features, feature => feature.Id == "mpoly");
        Assert.DoesNotContain(features, feature => feature.Id == "null-geometry");
    }

    [Fact]
    public async Task ReadsDeclaredLegacyEpsgButDoesNotInventOne()
    {
        var path = Fixture("legacy-crs.geojson");
        var metadata = await _reader.ReadMetadataAsync(path).ConfigureAwait(false);
        var layer = Assert.IsType<VectorLayerMetadata>(Assert.Single(metadata.Layers));

        Assert.Equal("EPSG", layer.SpatialReference.Authority);
        Assert.Equal("3857", layer.SpatialReference.Code);
        Assert.False(layer.SpatialReference.IsUnknown);
    }

    [Fact]
    public async Task RejectsOpenPolygonRingInsteadOfAutoClosingIt()
    {
        var path = Fixture("malformed-open-ring.geojson");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () =>
            {
                await _reader.ReadMetadataAsync(path).ConfigureAwait(false);
            }).ConfigureAwait(false);

        Assert.Contains("not closed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("coordinates", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidJsonHasActionableDiagnostic()
    {
        var path = Fixture("malformed-json.geojson");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () =>
            {
                await _reader.ReadMetadataAsync(path).ConfigureAwait(false);
            }).ConfigureAwait(false);

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("not valid JSON", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadsGeneratedLargeFixtureWithoutChangingCoordinates()
    {
        var path = await WriteLargeFixtureAsync(4096).ConfigureAwait(false);

        try
        {
            var metadata = await _reader.ReadMetadataAsync(path).ConfigureAwait(false);
            var layer = Assert.IsType<VectorLayerMetadata>(Assert.Single(metadata.Layers));

            Assert.Equal(4096L, layer.FeatureCount);
            Assert.Equal(GisGeometryType.Point, layer.GeometryType);
            Assert.Equal(new Envelope2D(0, 0, 63, 63), layer.Bounds);

            var features = await ReadAllAsync(path, layer.Name).ConfigureAwait(false);
            Assert.Equal(4096, features.Count);

            var last = Assert.IsType<PointGeometry>(features[^1].Geometry);
            Assert.Equal(new GisCoordinate(63, 63), last.Coordinate);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SupportsTopLevelFeatureAndGeometry()
    {
        var featurePath = await WriteTemporaryAsync(
            """
            {
              "type": "Feature",
              "id": "single",
              "geometry": { "type": "Point", "coordinates": [9, 8] },
              "properties": { "name": "single feature" }
            }
            """).ConfigureAwait(false);
        var geometryPath = await WriteTemporaryAsync(
            """
            {
              "type": "LineString",
              "coordinates": [[1, 1], [2, 2]]
            }
            """).ConfigureAwait(false);

        try
        {
            var featureMetadata = await _reader.ReadMetadataAsync(featurePath).ConfigureAwait(false);
            var featureLayer = Assert.IsType<VectorLayerMetadata>(Assert.Single(featureMetadata.Layers));
            var feature = Assert.Single(
                await ReadAllAsync(featurePath, featureLayer.Name).ConfigureAwait(false));
            Assert.Equal("single", feature.Id);

            var geometryMetadata = await _reader.ReadMetadataAsync(geometryPath).ConfigureAwait(false);
            var geometryLayer = Assert.IsType<VectorLayerMetadata>(Assert.Single(geometryMetadata.Layers));
            var geometryFeature = Assert.Single(
                await ReadAllAsync(geometryPath, geometryLayer.Name).ConfigureAwait(false));
            Assert.IsType<LineStringGeometry>(geometryFeature.Geometry);
            Assert.Empty(geometryFeature.Attributes);
        }
        finally
        {
            File.Delete(featurePath);
            File.Delete(geometryPath);
        }
    }

    [Fact]
    public async Task RejectsFourthOrdinateInsteadOfDiscardingIt()
    {
        var path = await WriteTemporaryAsync(
            """
            {
              "type": "Point",
              "coordinates": [1, 2, 3, 4]
            }
            """).ConfigureAwait(false);

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                async () =>
                {
                    await _reader.ReadMetadataAsync(path).ConfigureAwait(false);
                }).ConfigureAwait(false);

            Assert.Contains("silently discarded", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GeoJsonReaderFeedsRenderFrameEndToEnd()
    {
        var path = Fixture("all-geometries.geojson");
        var metadata = await _reader.ReadMetadataAsync(path).ConfigureAwait(false);
        var layer = Assert.IsType<VectorLayerMetadata>(Assert.Single(metadata.Layers));

        var frame = await GisVectorRenderFrameBuilder.BuildAsync(
            _reader.ReadFeaturesAsync(path, layer.Name),
            new Envelope2D(-10, -10, 30, 30)).ConfigureAwait(false);

        Assert.Equal(11, frame.Primitives.Count);
        Assert.Equal(4, frame.Primitives.Count(item => item.Kind == GisRenderPrimitiveKind.Point));
        Assert.Equal(4, frame.Primitives.Count(item => item.Kind == GisRenderPrimitiveKind.Polyline));
        Assert.Equal(3, frame.Primitives.Count(item => item.Kind == GisRenderPrimitiveKind.Polygon));
    }

    private async Task<List<GisFeature>> ReadAllAsync(
        string path,
        string layerName,
        Envelope2D? extent = null)
    {
        var result = new List<GisFeature>();

        await foreach (var feature in _reader
            .ReadFeaturesAsync(path, layerName, extent)
            .ConfigureAwait(false))
        {
            result.Add(feature);
        }

        return result;
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "geojson", name);

    private static async Task<string> WriteTemporaryAsync(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.geojson");
        await File.WriteAllTextAsync(path, content).ConfigureAwait(false);
        return path;
    }

    private static async Task<string> WriteLargeFixtureAsync(int featureCount)
    {
        var features = Enumerable.Range(0, featureCount)
            .Select(index => new
            {
                type = "Feature",
                id = index,
                geometry = new
                {
                    type = "Point",
                    coordinates = new[] { index % 64, index / 64 },
                },
                properties = new
                {
                    index,
                },
            })
            .ToArray();

        var content = JsonSerializer.Serialize(new
        {
            type = "FeatureCollection",
            features,
        });

        return await WriteTemporaryAsync(content).ConfigureAwait(false);
    }
}
