using SpatialViewer.Gis.Core;

namespace SpatialViewer.Gis.Rendering;

public static class GisVectorRenderFrameBuilder
{
    public static async ValueTask<GisRenderFrame> BuildAsync(
        IAsyncEnumerable<GisFeature> features,
        Envelope2D viewExtent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(features);

        if (!viewExtent.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(viewExtent),
                viewExtent,
                "View extent must be valid.");
        }

        var primitives = new List<GisRenderPrimitive>();

        await foreach (var feature in features.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (feature.Geometry is null)
            {
                continue;
            }

            AddGeometry(
                feature.Geometry,
                viewExtent,
                feature.Id,
                feature.Attributes,
                primitives);
        }

        return new GisRenderFrame(viewExtent, primitives);
    }

    private static void AddGeometry(
        IGisGeometry geometry,
        Envelope2D viewExtent,
        string? featureId,
        IReadOnlyDictionary<string, object?> attributes,
        List<GisRenderPrimitive> primitives)
    {
        if (geometry.Bounds is not { } bounds || !bounds.Intersects(viewExtent))
        {
            return;
        }

        switch (geometry)
        {
            case PointGeometry point:
                AddPoint(point.Coordinate, featureId, attributes, primitives);
                break;

            case MultiPointGeometry multiPoint:
                foreach (var coordinate in multiPoint.Coordinates)
                {
                    AddPoint(coordinate, featureId, attributes, primitives);
                }

                break;

            case LineStringGeometry lineString:
                AddPolyline(lineString.Coordinates, featureId, attributes, primitives);
                break;

            case MultiLineStringGeometry multiLineString:
                foreach (var line in multiLineString.Lines)
                {
                    AddPolyline(line, featureId, attributes, primitives);
                }

                break;

            case PolygonGeometry polygon:
                AddPolygon(polygon.Rings, featureId, attributes, primitives);
                break;

            case MultiPolygonGeometry multiPolygon:
                foreach (var polygonRings in multiPolygon.Polygons)
                {
                    AddPolygon(polygonRings, featureId, attributes, primitives);
                }

                break;

            case GeometryCollectionGeometry collection:
                foreach (var child in collection.Geometries)
                {
                    AddGeometry(child, viewExtent, featureId, attributes, primitives);
                }

                break;

            default:
                throw new NotSupportedException(
                    $"Geometry type '{geometry.GetType().FullName}' has no render primitive converter.");
        }
    }

    private static void AddPoint(
        GisCoordinate coordinate,
        string? featureId,
        IReadOnlyDictionary<string, object?> attributes,
        List<GisRenderPrimitive> primitives)
    {
        var bounds = new Envelope2D(coordinate.X, coordinate.Y, coordinate.X, coordinate.Y);
        primitives.Add(new GisRenderPrimitive(
            GisRenderPrimitiveKind.Point,
            bounds,
            new GisPointRenderData(coordinate),
            featureId,
            attributes));
    }

    private static void AddPolyline(
        IReadOnlyList<GisCoordinate> coordinates,
        string? featureId,
        IReadOnlyDictionary<string, object?> attributes,
        List<GisRenderPrimitive> primitives)
    {
        if (TryCalculateBounds(coordinates) is not { } bounds)
        {
            return;
        }

        primitives.Add(new GisRenderPrimitive(
            GisRenderPrimitiveKind.Polyline,
            bounds,
            new GisPolylineRenderData(coordinates),
            featureId,
            attributes));
    }

    private static void AddPolygon(
        IReadOnlyList<IReadOnlyList<GisCoordinate>> rings,
        string? featureId,
        IReadOnlyDictionary<string, object?> attributes,
        List<GisRenderPrimitive> primitives)
    {
        Envelope2D? bounds = null;

        foreach (var ring in rings)
        {
            var ringBounds = TryCalculateBounds(ring);
            if (ringBounds is null)
            {
                continue;
            }

            bounds = bounds is null
                ? ringBounds
                : Envelope2D.Union(bounds.Value, ringBounds.Value);
        }

        if (bounds is null)
        {
            return;
        }

        primitives.Add(new GisRenderPrimitive(
            GisRenderPrimitiveKind.Polygon,
            bounds.Value,
            new GisPolygonRenderData(rings),
            featureId,
            attributes));
    }

    private static Envelope2D? TryCalculateBounds(IReadOnlyList<GisCoordinate> coordinates)
    {
        if (coordinates.Count == 0)
        {
            return null;
        }

        var first = coordinates[0];
        var minX = first.X;
        var minY = first.Y;
        var maxX = first.X;
        var maxY = first.Y;

        for (var index = 1; index < coordinates.Count; index++)
        {
            var coordinate = coordinates[index];
            minX = Math.Min(minX, coordinate.X);
            minY = Math.Min(minY, coordinate.Y);
            maxX = Math.Max(maxX, coordinate.X);
            maxY = Math.Max(maxY, coordinate.Y);
        }

        return new Envelope2D(minX, minY, maxX, maxY);
    }
}
