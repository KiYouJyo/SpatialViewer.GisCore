using SpatialViewer.Gis.Core;
using SpatialViewer.Gis.Rendering;
using Xunit;

namespace SpatialViewer.Gis.Rendering.Tests;

public sealed class VectorRenderFrameBuilderTests
{
    [Fact]
    public async Task FlattensMultiAndCollectionGeometryIntoTypedPrimitives()
    {
        var attributes = new Dictionary<string, object?> { ["name"] = "mixed" };
        var geometry = new GeometryCollectionGeometry(
            new IGisGeometry[]
            {
                new MultiPointGeometry(
                    new[]
                    {
                        new GisCoordinate(1, 1),
                        new GisCoordinate(2, 2),
                    }),
                new MultiLineStringGeometry(
                    new IReadOnlyList<GisCoordinate>[]
                    {
                        new[]
                        {
                            new GisCoordinate(0, 0),
                            new GisCoordinate(3, 3),
                        },
                        new[]
                        {
                            new GisCoordinate(4, 4),
                            new GisCoordinate(5, 5),
                        },
                    }),
                new MultiPolygonGeometry(
                    new IReadOnlyList<IReadOnlyList<GisCoordinate>>[]
                    {
                        new IReadOnlyList<GisCoordinate>[]
                        {
                            new[]
                            {
                                new GisCoordinate(0, 0),
                                new GisCoordinate(1, 0),
                                new GisCoordinate(1, 1),
                                new GisCoordinate(0, 0),
                            },
                        },
                        new IReadOnlyList<GisCoordinate>[]
                        {
                            new[]
                            {
                                new GisCoordinate(6, 6),
                                new GisCoordinate(7, 6),
                                new GisCoordinate(7, 7),
                                new GisCoordinate(6, 6),
                            },
                        },
                    }),
            });

        var feature = new GisFeature("feature-1", geometry, attributes);
        var frame = await GisVectorRenderFrameBuilder.BuildAsync(
            ToAsync(feature),
            new Envelope2D(-1, -1, 10, 10)).ConfigureAwait(false);

        Assert.Equal(6, frame.Primitives.Count);
        Assert.Equal(2, frame.Primitives.Count(item => item.Kind == GisRenderPrimitiveKind.Point));
        Assert.Equal(2, frame.Primitives.Count(item => item.Kind == GisRenderPrimitiveKind.Polyline));
        Assert.Equal(2, frame.Primitives.Count(item => item.Kind == GisRenderPrimitiveKind.Polygon));
        Assert.All(frame.Primitives, item => Assert.Equal("feature-1", item.FeatureId));
        Assert.All(frame.Primitives, item => Assert.Same(attributes, item.Attributes));

        Assert.All(
            frame.Primitives.Where(item => item.Kind == GisRenderPrimitiveKind.Point),
            item => Assert.IsType<GisPointRenderData>(item.Payload));
        Assert.All(
            frame.Primitives.Where(item => item.Kind == GisRenderPrimitiveKind.Polyline),
            item => Assert.IsType<GisPolylineRenderData>(item.Payload));
        Assert.All(
            frame.Primitives.Where(item => item.Kind == GisRenderPrimitiveKind.Polygon),
            item => Assert.IsType<GisPolygonRenderData>(item.Payload));
    }

    [Fact]
    public async Task SkipsNullEmptyAndOutsideGeometry()
    {
        var attributes = new Dictionary<string, object?>();
        var features = new[]
        {
            new GisFeature("null", null, attributes),
            new GisFeature("empty", new LineStringGeometry(Array.Empty<GisCoordinate>()), attributes),
            new GisFeature("outside", new PointGeometry(new GisCoordinate(100, 100)), attributes),
            new GisFeature("inside", new PointGeometry(new GisCoordinate(5, 5)), attributes),
        };

        var frame = await GisVectorRenderFrameBuilder.BuildAsync(
            ToAsync(features),
            new Envelope2D(0, 0, 10, 10)).ConfigureAwait(false);

        var primitive = Assert.Single(frame.Primitives);
        Assert.Equal("inside", primitive.FeatureId);
        Assert.Equal(new Envelope2D(5, 5, 5, 5), primitive.Bounds);
    }

    private static async IAsyncEnumerable<GisFeature> ToAsync(params GisFeature[] features)
    {
        foreach (var feature in features)
        {
            await Task.Yield();
            yield return feature;
        }
    }
}
