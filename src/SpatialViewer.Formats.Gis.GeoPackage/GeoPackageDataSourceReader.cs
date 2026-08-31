using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using SpatialViewer.Formats.Gis;
using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis.GeoPackage;

public sealed class GeoPackageDataSourceReader : IGisDataSourceReader
{
    private const long GeoPackageApplicationId = 0x47504B47;

    public string FormatId => GeoPackageFormatProbe.FormatId;

    public async ValueTask<GisDatasetMetadata> ReadMetadataAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ValidatePath(path);
        await using var connection = await OpenConnectionAsync(fullPath, cancellationToken).ConfigureAwait(false);
        await ValidateGeoPackageAsync(connection, cancellationToken).ConfigureAwait(false);
        var descriptors = await ReadLayerDescriptorsAsync(connection, cancellationToken).ConfigureAwait(false);
        var layers = new List<GisLayerMetadata>(descriptors.Count);

        foreach (var descriptor in descriptors)
        {
            var featureCount = await ReadFeatureCountAsync(connection, descriptor.TableName, cancellationToken)
                .ConfigureAwait(false);
            layers.Add(new VectorLayerMetadata(
                descriptor.TableName,
                descriptor.SpatialReference,
                descriptor.Bounds,
                MapGeometryType(descriptor.GeometryTypeName),
                featureCount));
        }

        return new GisDatasetMetadata(Path.GetFileName(fullPath), FormatId, layers);
    }

    public async IAsyncEnumerable<GisFeature> ReadFeaturesAsync(
        string path,
        string layerName,
        Envelope2D? extent = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
        var fullPath = ValidatePath(path);
        await using var connection = await OpenConnectionAsync(fullPath, cancellationToken).ConfigureAwait(false);
        await ValidateGeoPackageAsync(connection, cancellationToken).ConfigureAwait(false);
        var descriptors = await ReadLayerDescriptorsAsync(connection, cancellationToken).ConfigureAwait(false);
        var descriptor = descriptors.Find(
            item => string.Equals(item.TableName, layerName, StringComparison.Ordinal));

        if (descriptor is null)
        {
            throw new ArgumentException($"GeoPackage feature layer '{layerName}' was not found.", nameof(layerName));
        }

        var primaryKey = await ReadPrimaryKeyColumnAsync(connection, descriptor.TableName, cancellationToken)
            .ConfigureAwait(false);
        var rtreeName = $"rtree_{descriptor.TableName}_{descriptor.GeometryColumn}";
        var hasRtree = extent is not null &&
            await TableExistsAsync(connection, rtreeName, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = BuildFeatureQuery(descriptor, primaryKey, rtreeName, extent, hasRtree);

        if (extent is not null && hasRtree)
        {
            command.Parameters.AddWithValue("$minX", extent.Value.MinX);
            command.Parameters.AddWithValue("$maxX", extent.Value.MaxX);
            command.Parameters.AddWithValue("$minY", extent.Value.MinY);
            command.Parameters.AddWithValue("$maxY", extent.Value.MaxY);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var geometryOrdinal = reader.GetOrdinal(descriptor.GeometryColumn);
        var primaryKeyOrdinal = reader.GetOrdinal(primaryKey);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IGisGeometry? geometry = null;
            GisBoundingBox? declaredBounds = null;

            if (!reader.IsDBNull(geometryOrdinal))
            {
                var blob = reader.GetFieldValue<byte[]>(geometryOrdinal);
                var parsed = GeoPackageGeometryReader.Parse(blob);
                if (parsed.SpatialReferenceId != descriptor.SpatialReferenceId)
                {
                    throw new InvalidDataException(
                        $"GeoPackage geometry SRS_ID {parsed.SpatialReferenceId} does not match layer SRS_ID {descriptor.SpatialReferenceId} in '{descriptor.TableName}'.");
                }

                geometry = parsed.Geometry;
                declaredBounds = parsed.DeclaredBounds;
            }

            if (extent is not null && !hasRtree &&
                (geometry?.Bounds is null || !geometry.Bounds.Value.Intersects(extent.Value)))
            {
                continue;
            }

            var attributes = ReadAttributes(reader, geometryOrdinal);
            var idValue = reader.GetValue(primaryKeyOrdinal);
            var id = Convert.ToString(idValue, CultureInfo.InvariantCulture);

            yield return new GisFeature(id, geometry, attributes, declaredBounds);
        }
    }

    private static async ValueTask<SqliteConnection> OpenConnectionAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async ValueTask ValidateGeoPackageAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA application_id;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var applicationId = result is null or DBNull
            ? 0L
            : Convert.ToInt64(result, CultureInfo.InvariantCulture);

        if (applicationId != GeoPackageApplicationId)
        {
            throw new InvalidDataException(
                $"SQLite file is not a conforming GeoPackage: application_id is 0x{applicationId:X8}, expected 0x{GeoPackageApplicationId:X8}.");
        }
    }

    private static async ValueTask<List<GeoPackageLayerDescriptor>> ReadLayerDescriptorsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT gc.table_name,
                   gc.column_name,
                   gc.geometry_type_name,
                   gc.srs_id,
                   c.min_x,
                   c.min_y,
                   c.max_x,
                   c.max_y,
                   s.srs_name,
                   s.organization,
                   s.organization_coordsys_id,
                   s.definition
              FROM gpkg_geometry_columns AS gc
              JOIN gpkg_contents AS c
                ON c.table_name = gc.table_name
              LEFT JOIN gpkg_spatial_ref_sys AS s
                ON s.srs_id = gc.srs_id
             WHERE c.data_type = 'features'
             ORDER BY gc.table_name;
            """;

        var result = new List<GeoPackageLayerDescriptor>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var tableName = reader.GetString(0);
            var geometryColumn = reader.GetString(1);
            var geometryTypeName = reader.GetString(2);
            var srsId = checked((int)reader.GetInt64(3));
            var bounds = ReadOptionalBounds(reader, 4);
            var srsName = reader.IsDBNull(8) ? null : reader.GetString(8);
            var organization = reader.IsDBNull(9) ? null : reader.GetString(9);
            var organizationCode = reader.IsDBNull(10)
                ? (long?)null
                : reader.GetInt64(10);
            var definition = reader.IsDBNull(11) ? null : reader.GetString(11);
            var wellKnownText = string.IsNullOrWhiteSpace(definition) ||
                                string.Equals(definition, "undefined", StringComparison.OrdinalIgnoreCase)
                ? null
                : definition;
            var code = organizationCode is > 0
                ? organizationCode.Value.ToString(CultureInfo.InvariantCulture)
                : null;
            var spatialReference = new SpatialReference(organization, code, wellKnownText, srsName);

            result.Add(new GeoPackageLayerDescriptor(
                tableName,
                geometryColumn,
                geometryTypeName,
                srsId,
                bounds,
                spatialReference));
        }

        return result;
    }

    private static Envelope2D? ReadOptionalBounds(SqliteDataReader reader, int startOrdinal)
    {
        if (reader.IsDBNull(startOrdinal) ||
            reader.IsDBNull(startOrdinal + 1) ||
            reader.IsDBNull(startOrdinal + 2) ||
            reader.IsDBNull(startOrdinal + 3))
        {
            return null;
        }

        var bounds = new Envelope2D(
            reader.GetDouble(startOrdinal),
            reader.GetDouble(startOrdinal + 1),
            reader.GetDouble(startOrdinal + 2),
            reader.GetDouble(startOrdinal + 3));
        return bounds.IsValid ? bounds : null;
    }

    private static async ValueTask<long> ReadFeatureCountAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)};";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async ValueTask<string> ReadPrimaryKeyColumnAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        string? primaryKey = null;
        long primaryKeyRank = long.MaxValue;

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var rank = reader.GetInt64(5);
            if (rank > 0 && rank < primaryKeyRank)
            {
                primaryKey = reader.GetString(1);
                primaryKeyRank = rank;
            }
        }

        return primaryKey ?? throw new InvalidDataException(
            $"GeoPackage feature table '{tableName}' does not declare a primary key column.");
    }

    private static async ValueTask<bool> TableExistsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name);";
        command.Parameters.AddWithValue("$name", tableName);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture) != 0;
    }

    private static string BuildFeatureQuery(
        GeoPackageLayerDescriptor descriptor,
        string primaryKey,
        string rtreeName,
        Envelope2D? extent,
        bool hasRtree)
    {
        var table = QuoteIdentifier(descriptor.TableName);
        if (extent is null || !hasRtree)
        {
            return $"SELECT * FROM {table};";
        }

        var pk = QuoteIdentifier(primaryKey);
        var rtree = QuoteIdentifier(rtreeName);
        return
            $"SELECT f.* FROM {table} AS f JOIN {rtree} AS r ON f.{pk} = r.id " +
            "WHERE r.maxx >= $minX AND r.minx <= $maxX AND r.maxy >= $minY AND r.miny <= $maxY;";
    }

    private static Dictionary<string, object?> ReadAttributes(
        SqliteDataReader reader,
        int geometryOrdinal)
    {
        var attributes = new Dictionary<string, object?>(reader.FieldCount - 1, StringComparer.Ordinal);

        for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
        {
            if (ordinal == geometryOrdinal)
            {
                continue;
            }

            attributes.Add(
                reader.GetName(ordinal),
                reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal));
        }

        return attributes;
    }

    private static GisGeometryType? MapGeometryType(string geometryTypeName) =>
        geometryTypeName.ToUpperInvariant() switch
        {
            "POINT" => GisGeometryType.Point,
            "MULTIPOINT" => GisGeometryType.MultiPoint,
            "LINESTRING" => GisGeometryType.LineString,
            "MULTILINESTRING" => GisGeometryType.MultiLineString,
            "POLYGON" => GisGeometryType.Polygon,
            "MULTIPOLYGON" => GisGeometryType.MultiPolygon,
            "GEOMETRYCOLLECTION" or "GEOMCOLLECTION" => GisGeometryType.GeometryCollection,
            "GEOMETRY" => null,
            _ => null,
        };

    private static string ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);

        if (!Path.GetExtension(fullPath).Equals(".gpkg", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("GeoPackage reader requires a .gpkg path.", nameof(path));
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"GeoPackage file '{fullPath}' was not found.", fullPath);
        }

        return fullPath;
    }

    private static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private sealed record GeoPackageLayerDescriptor(
        string TableName,
        string GeometryColumn,
        string GeometryTypeName,
        int SpatialReferenceId,
        Envelope2D? Bounds,
        SpatialReference SpatialReference);
}
