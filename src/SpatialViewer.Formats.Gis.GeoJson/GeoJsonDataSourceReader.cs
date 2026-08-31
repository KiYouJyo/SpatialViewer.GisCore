using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SpatialViewer.Formats.Gis;
using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis.GeoJson;

public sealed class GeoJsonDataSourceReader : IGisDataSourceReader
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyAttributes =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public string FormatId => GeoJsonFormatProbe.FormatId;

    public async ValueTask<GisDatasetMetadata> ReadMetadataAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var document = await ReadDocumentAsync(path, cancellationToken).ConfigureAwait(false);

        var layer = new VectorLayerMetadata(
            document.LayerName,
            document.SpatialReference,
            document.DeclaredBounds?.XY ?? CalculateBounds(document.Features),
            CalculateGeometryType(document.Features),
            document.Features.Count);

        return new GisDatasetMetadata(
            Path.GetFileName(path),
            FormatId,
            new GisLayerMetadata[] { layer });
    }

    public async IAsyncEnumerable<GisFeature> ReadFeaturesAsync(
        string path,
        string layerName,
        Envelope2D? extent = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerName);

        var document = await ReadDocumentAsync(path, cancellationToken).ConfigureAwait(false);

        if (!string.Equals(layerName, document.LayerName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"GeoJSON layer '{layerName}' was not found. Expected '{document.LayerName}'.",
                nameof(layerName));
        }

        foreach (var feature in document.Features)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (extent is not null &&
                (feature.Bounds is null || !feature.Bounds.Value.Intersects(extent.Value)))
            {
                continue;
            }

            yield return feature;
        }
    }

    private static async ValueTask<GeoJsonDocumentData> ReadDocumentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            using var json = await JsonDocument.ParseAsync(
                stream,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 128,
                },
                cancellationToken).ConfigureAwait(false);

            return ParseDocument(json.RootElement, path);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"The GeoJSON file '{path}' is not valid JSON: {exception.Message}",
                exception);
        }
    }

    private static GeoJsonDocumentData ParseDocument(JsonElement root, string path)
    {
        EnsureObject(root, "GeoJSON root");

        var layerName = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(layerName))
        {
            layerName = "GeoJSON";
        }

        var spatialReference = ParseSpatialReference(root);
        var declaredBounds = ParseOptionalBoundingBox(root, "root");
        var type = GetRequiredString(root, "type", "GeoJSON root");

        return type switch
        {
            "FeatureCollection" => ParseFeatureCollection(
                root,
                layerName,
                spatialReference,
                declaredBounds),
            "Feature" => new GeoJsonDocumentData(
                layerName,
                spatialReference,
                declaredBounds,
                new[] { ParseFeature(root, "root") }),
            "Point" or
            "MultiPoint" or
            "LineString" or
            "MultiLineString" or
            "Polygon" or
            "MultiPolygon" or
            "GeometryCollection" => new GeoJsonDocumentData(
                layerName,
                spatialReference,
                declaredBounds,
                new[]
                {
                    new GisFeature(
                        null,
                        ParseGeometry(root, "root"),
                        EmptyAttributes,
                        declaredBounds),
                }),
            _ => throw Invalid("root.type", $"Unsupported GeoJSON object type '{type}'."),
        };
    }

    private static GeoJsonDocumentData ParseFeatureCollection(
        JsonElement root,
        string layerName,
        SpatialReference spatialReference,
        GisBoundingBox? declaredBounds)
    {
        if (!root.TryGetProperty("features", out var featuresElement) ||
            featuresElement.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("root.features", "FeatureCollection requires an array 'features' member.");
        }

        var features = new List<GisFeature>(featuresElement.GetArrayLength());
        var index = 0;

        foreach (var featureElement in featuresElement.EnumerateArray())
        {
            features.Add(ParseFeature(featureElement, $"root.features[{index}]"));
            index++;
        }

        return new GeoJsonDocumentData(
            layerName,
            spatialReference,
            declaredBounds,
            features);
    }

    private static GisFeature ParseFeature(JsonElement element, string context)
    {
        EnsureObject(element, context);

        var type = GetRequiredString(element, "type", context);
        if (!string.Equals(type, "Feature", StringComparison.Ordinal))
        {
            throw Invalid($"{context}.type", $"Expected 'Feature' but found '{type}'.");
        }

        if (!element.TryGetProperty("geometry", out var geometryElement))
        {
            throw Invalid($"{context}.geometry", "Feature requires a 'geometry' member.");
        }

        IGisGeometry? geometry = geometryElement.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Object => ParseGeometry(geometryElement, $"{context}.geometry"),
            _ => throw Invalid($"{context}.geometry", "Feature geometry must be an object or null."),
        };

        if (!element.TryGetProperty("properties", out var propertiesElement))
        {
            throw Invalid($"{context}.properties", "Feature requires a 'properties' member.");
        }

        var attributes = propertiesElement.ValueKind switch
        {
            JsonValueKind.Null => EmptyAttributes,
            JsonValueKind.Object => ParseProperties(propertiesElement, $"{context}.properties"),
            _ => throw Invalid($"{context}.properties", "Feature properties must be an object or null."),
        };

        var id = ParseOptionalFeatureId(element, context);
        var declaredBounds = ParseOptionalBoundingBox(element, context);

        return new GisFeature(id, geometry, attributes, declaredBounds);
    }

    private static IGisGeometry ParseGeometry(JsonElement element, string context)
    {
        EnsureObject(element, context);

        var type = GetRequiredString(element, "type", context);
        var declaredBounds = ParseOptionalBoundingBox(element, context);

        return type switch
        {
            "Point" => new PointGeometry(
                ParsePosition(GetCoordinates(element, context), $"{context}.coordinates"),
                declaredBounds),
            "MultiPoint" => new MultiPointGeometry(
                ParsePositions(GetCoordinates(element, context), $"{context}.coordinates"),
                declaredBounds),
            "LineString" => new LineStringGeometry(
                ParseLineString(GetCoordinates(element, context), $"{context}.coordinates"),
                declaredBounds),
            "MultiLineString" => new MultiLineStringGeometry(
                ParseMultiLineString(GetCoordinates(element, context), $"{context}.coordinates"),
                declaredBounds),
            "Polygon" => new PolygonGeometry(
                ParsePolygon(GetCoordinates(element, context), $"{context}.coordinates"),
                declaredBounds),
            "MultiPolygon" => new MultiPolygonGeometry(
                ParseMultiPolygon(GetCoordinates(element, context), $"{context}.coordinates"),
                declaredBounds),
            "GeometryCollection" => new GeometryCollectionGeometry(
                ParseGeometryCollection(element, context),
                declaredBounds),
            _ => throw Invalid($"{context}.type", $"Unsupported geometry type '{type}'."),
        };
    }

    private static JsonElement GetCoordinates(JsonElement geometry, string context)
    {
        if (!geometry.TryGetProperty("coordinates", out var coordinates))
        {
            throw Invalid($"{context}.coordinates", "Geometry requires a 'coordinates' member.");
        }

        if (coordinates.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"{context}.coordinates", "Geometry coordinates must be an array.");
        }

        return coordinates;
    }

    private static IReadOnlyList<IGisGeometry> ParseGeometryCollection(
        JsonElement geometry,
        string context)
    {
        if (!geometry.TryGetProperty("geometries", out var geometries) ||
            geometries.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"{context}.geometries", "GeometryCollection requires an array 'geometries' member.");
        }

        var result = new List<IGisGeometry>(geometries.GetArrayLength());
        var index = 0;

        foreach (var child in geometries.EnumerateArray())
        {
            if (child.ValueKind != JsonValueKind.Object)
            {
                throw Invalid($"{context}.geometries[{index}]", "GeometryCollection members must be geometry objects.");
            }

            result.Add(ParseGeometry(child, $"{context}.geometries[{index}]"));
            index++;
        }

        return result;
    }

    private static GisCoordinate ParsePosition(JsonElement position, string context)
    {
        if (position.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(context, "A GeoJSON position must be an array.");
        }

        var dimension = position.GetArrayLength();
        if (dimension is < 2 or > 3)
        {
            throw Invalid(
                context,
                $"A GeoJSON position must contain 2 or 3 ordinates; found {dimension}. Extra ordinates are not silently discarded.");
        }

        var ordinates = position.EnumerateArray().ToArray();
        var x = ParseFiniteNumber(ordinates[0], $"{context}[0]");
        var y = ParseFiniteNumber(ordinates[1], $"{context}[1]");
        var z = dimension == 3
            ? ParseFiniteNumber(ordinates[2], $"{context}[2]")
            : (double?)null;

        return new GisCoordinate(x, y, z);
    }

    private static IReadOnlyList<GisCoordinate> ParsePositions(JsonElement coordinates, string context)
    {
        var result = new List<GisCoordinate>(coordinates.GetArrayLength());
        var index = 0;

        foreach (var position in coordinates.EnumerateArray())
        {
            result.Add(ParsePosition(position, $"{context}[{index}]"));
            index++;
        }

        return result;
    }

    private static IReadOnlyList<GisCoordinate> ParseLineString(JsonElement coordinates, string context)
    {
        var result = ParsePositions(coordinates, context);
        if (result.Count != 0 && result.Count < 2)
        {
            throw Invalid(context, "LineString must contain at least two positions or be empty.");
        }

        return result;
    }

    private static IReadOnlyList<IReadOnlyList<GisCoordinate>> ParseMultiLineString(
        JsonElement coordinates,
        string context)
    {
        var result = new List<IReadOnlyList<GisCoordinate>>(coordinates.GetArrayLength());
        var index = 0;

        foreach (var line in coordinates.EnumerateArray())
        {
            if (line.ValueKind != JsonValueKind.Array)
            {
                throw Invalid($"{context}[{index}]", "MultiLineString members must be coordinate arrays.");
            }

            result.Add(ParseLineString(line, $"{context}[{index}]"));
            index++;
        }

        return result;
    }

    private static IReadOnlyList<IReadOnlyList<GisCoordinate>> ParsePolygon(
        JsonElement coordinates,
        string context)
    {
        var rings = new List<IReadOnlyList<GisCoordinate>>(coordinates.GetArrayLength());
        var index = 0;

        foreach (var ringElement in coordinates.EnumerateArray())
        {
            if (ringElement.ValueKind != JsonValueKind.Array)
            {
                throw Invalid($"{context}[{index}]", "Polygon rings must be coordinate arrays.");
            }

            var ring = ParsePositions(ringElement, $"{context}[{index}]");
            ValidateLinearRing(ring, $"{context}[{index}]");
            rings.Add(ring);
            index++;
        }

        return rings;
    }

    private static IReadOnlyList<IReadOnlyList<IReadOnlyList<GisCoordinate>>> ParseMultiPolygon(
        JsonElement coordinates,
        string context)
    {
        var polygons = new List<IReadOnlyList<IReadOnlyList<GisCoordinate>>>(coordinates.GetArrayLength());
        var index = 0;

        foreach (var polygonElement in coordinates.EnumerateArray())
        {
            if (polygonElement.ValueKind != JsonValueKind.Array)
            {
                throw Invalid($"{context}[{index}]", "MultiPolygon members must be polygon coordinate arrays.");
            }

            polygons.Add(ParsePolygon(polygonElement, $"{context}[{index}]"));
            index++;
        }

        return polygons;
    }

    private static void ValidateLinearRing(IReadOnlyList<GisCoordinate> ring, string context)
    {
        if (ring.Count == 0)
        {
            return;
        }

        if (ring.Count < 4)
        {
            throw Invalid(context, "A non-empty Polygon ring must contain at least four positions.");
        }

        var first = ring[0];
        var last = ring[^1];
        if (first.X != last.X || first.Y != last.Y || first.Z != last.Z)
        {
            throw Invalid(context, "Polygon ring is not closed. Coordinates are never auto-closed.");
        }
    }

    private static IReadOnlyDictionary<string, object?> ParseProperties(
        JsonElement properties,
        string context)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in properties.EnumerateObject())
        {
            if (!result.TryAdd(
                property.Name,
                ParsePropertyValue(property.Value, $"{context}.{property.Name}")))
            {
                throw Invalid(context, $"Duplicate property name '{property.Name}' is ambiguous.");
            }
        }

        return result;
    }

    private static object? ParsePropertyValue(JsonElement value, string context)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => ParsePropertyNumber(value, context),
            JsonValueKind.Array => ParsePropertyArray(value, context),
            JsonValueKind.Object => ParseProperties(value, context),
            _ => throw Invalid(context, $"Unsupported JSON value kind '{value.ValueKind}'."),
        };
    }

    private static object ParsePropertyNumber(JsonElement value, string context)
    {
        if (value.TryGetInt64(out var integer))
        {
            return integer;
        }

        if (value.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        return ParseFiniteNumber(value, context);
    }

    private static IReadOnlyList<object?> ParsePropertyArray(JsonElement value, string context)
    {
        var result = new List<object?>(value.GetArrayLength());
        var index = 0;

        foreach (var item in value.EnumerateArray())
        {
            result.Add(ParsePropertyValue(item, $"{context}[{index}]"));
            index++;
        }

        return result;
    }

    private static string? ParseOptionalFeatureId(JsonElement feature, string context)
    {
        if (!feature.TryGetProperty("id", out var id))
        {
            return null;
        }

        return id.ValueKind switch
        {
            JsonValueKind.String => id.GetString(),
            JsonValueKind.Number => id.GetRawText(),
            _ => throw Invalid($"{context}.id", "Feature id must be a string or number."),
        };
    }

    private static GisBoundingBox? ParseOptionalBoundingBox(JsonElement element, string context)
    {
        if (!element.TryGetProperty("bbox", out var bbox))
        {
            return null;
        }

        if (bbox.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"{context}.bbox", "bbox must be an array.");
        }

        var values = bbox.EnumerateArray()
            .Select((item, index) => ParseFiniteNumber(item, $"{context}.bbox[{index}]"))
            .ToArray();

        if (values.Length == 4)
        {
            var xy = new Envelope2D(values[0], values[1], values[2], values[3]);
            if (!xy.IsValid)
            {
                throw Invalid($"{context}.bbox", "2D bbox minimums must not exceed maximums.");
            }

            return new GisBoundingBox(xy);
        }

        if (values.Length == 6)
        {
            var xy = new Envelope2D(values[0], values[1], values[3], values[4]);
            if (!xy.IsValid || values[2] > values[5])
            {
                throw Invalid($"{context}.bbox", "3D bbox minimums must not exceed maximums.");
            }

            return new GisBoundingBox(xy, values[2], values[5]);
        }

        throw Invalid($"{context}.bbox", $"bbox must contain 4 or 6 numbers; found {values.Length}.");
    }

    private static SpatialReference ParseSpatialReference(JsonElement root)
    {
        if (!root.TryGetProperty("crs", out var crs) || crs.ValueKind == JsonValueKind.Null)
        {
            return SpatialReference.Unknown;
        }

        if (crs.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("root.crs", "Legacy GeoJSON crs must be an object or null.");
        }

        if (!crs.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            throw Invalid("root.crs.type", "Legacy GeoJSON crs requires a string 'type' member.");
        }

        if (!crs.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("root.crs.properties", "Legacy GeoJSON crs requires an object 'properties' member.");
        }

        var type = typeElement.GetString();
        if (string.Equals(type, "name", StringComparison.Ordinal) &&
            properties.TryGetProperty("name", out var nameElement) &&
            nameElement.ValueKind == JsonValueKind.String)
        {
            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name))
            {
                return SpatialReference.Unknown;
            }

            return ParseNamedSpatialReference(name);
        }

        return SpatialReference.Unknown;
    }

    private static SpatialReference ParseNamedSpatialReference(string name)
    {
        const string epsgPrefix = "EPSG:";
        if (name.StartsWith(epsgPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var code = name[epsgPrefix.Length..];
            if (!string.IsNullOrWhiteSpace(code))
            {
                return new SpatialReference("EPSG", code, Name: name);
            }
        }

        const string urnPrefix = "urn:ogc:def:crs:EPSG::";
        if (name.StartsWith(urnPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var code = name[urnPrefix.Length..];
            if (!string.IsNullOrWhiteSpace(code))
            {
                return new SpatialReference("EPSG", code, Name: name);
            }
        }

        return new SpatialReference(null, null, Name: name);
    }

    private static Envelope2D? CalculateBounds(IReadOnlyList<GisFeature> features)
    {
        Envelope2D? bounds = null;

        foreach (var feature in features)
        {
            bounds = Union(bounds, feature.Bounds);
        }

        return bounds;
    }

    private static GisGeometryType? CalculateGeometryType(IReadOnlyList<GisFeature> features)
    {
        GisGeometryType? geometryType = null;

        foreach (var feature in features)
        {
            if (feature.Geometry is null)
            {
                continue;
            }

            if (geometryType is null)
            {
                geometryType = feature.Geometry.GeometryType;
                continue;
            }

            if (geometryType.Value != feature.Geometry.GeometryType)
            {
                return null;
            }
        }

        return geometryType;
    }

    private static Envelope2D? Union(Envelope2D? left, Envelope2D? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return Envelope2D.Union(left.Value, right.Value);
    }

    private static string GetRequiredString(JsonElement element, string propertyName, string context)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"{context}.{propertyName}", $"'{propertyName}' must be a string.");
        }

        return property.GetString()!;
    }

    private static double ParseFiniteNumber(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out var value) ||
            !double.IsFinite(value))
        {
            throw Invalid(context, "Coordinate/bbox ordinate must be a finite JSON number.");
        }

        return value;
    }

    private static void EnsureObject(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(context, "GeoJSON object must be a JSON object.");
        }
    }

    private static InvalidDataException Invalid(string context, string message) =>
        new($"Invalid GeoJSON at {context}: {message}");

    private sealed record GeoJsonDocumentData(
        string LayerName,
        SpatialReference SpatialReference,
        GisBoundingBox? DeclaredBounds,
        IReadOnlyList<GisFeature> Features);
}
