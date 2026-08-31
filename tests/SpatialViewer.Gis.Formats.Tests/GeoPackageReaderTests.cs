using Microsoft.Data.Sqlite;
using SpatialViewer.Formats.Gis.GeoPackage;
using SpatialViewer.Gis.Core;
using Xunit;

namespace SpatialViewer.Gis.Formats.Tests;

public sealed class GeoPackageReaderTests
{
    private readonly GeoPackageDataSourceReader _reader = new();

    [Theory]
    [InlineData("sample.gpkg", true)]
    [InlineData("sample.GPKG", true)]
    [InlineData("sample.sqlite", false)]
    public async Task ProbeUsesGpkgExtension(string path, bool expected)
    {
        var result = await new GeoPackageFormatProbe().ProbeAsync(path);
        Assert.Equal(expected, result.IsMatch);
    }

    [Fact]
    public async Task ReadsFeatureTablesMetadataAndSpatialReference()
    {
        var path = await CreateFixtureAsync();
        try
        {
            var metadata = await _reader.ReadMetadataAsync(path);

            Assert.Equal("geopackage", metadata.SourceKind);
            Assert.Equal(2, metadata.Layers.Count);

            var places = Assert.IsType<VectorLayerMetadata>(metadata.Layers.Single(layer => layer.Name == "places"));
            Assert.Equal(3L, places.FeatureCount);
            Assert.Equal(GisGeometryType.Point, places.GeometryType);
            Assert.Equal(new Envelope2D(1, 2, 100, 100), places.Bounds);
            Assert.Equal("EPSG", places.SpatialReference.Authority);
            Assert.Equal("4326", places.SpatialReference.Code);

            var routes = Assert.IsType<VectorLayerMetadata>(metadata.Layers.Single(layer => layer.Name == "routes"));
            Assert.Equal(1L, routes.FeatureCount);
            Assert.Equal(GisGeometryType.LineString, routes.GeometryType);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PreservesGeoPackagePointZmAndAttributes()
    {
        var path = await CreateFixtureAsync();
        try
        {
            var features = await ReadAllAsync(path, "places");

            Assert.Equal(3, features.Count);
            var first = features.Single(feature => feature.Id == "1");
            var point = Assert.IsType<PointGeometry>(first.Geometry);
            Assert.Equal(new GisCoordinate(1, 2, 3, 4), point.Coordinate);
            Assert.Equal(new GisBoundingBox(new Envelope2D(1, 2, 1, 2), 3, 3), point.DeclaredBounds);
            Assert.Equal("東京", first.Attributes["name"]);
            Assert.Equal(12.5d, Assert.IsType<double>(first.Attributes["score"]));
            Assert.Null(first.Attributes["nullable"]);

            var nullGeometry = features.Single(feature => feature.Id == "3");
            Assert.Null(nullGeometry.Geometry);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UsesRtreeCompatibleExtentQuerySemantics()
    {
        var path = await CreateFixtureAsync();
        try
        {
            var features = await ReadAllAsync(path, "places", new Envelope2D(0, 0, 10, 10));

            var feature = Assert.Single(features);
            Assert.Equal("1", feature.Id);
            Assert.Equal("東京", feature.Attributes["name"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadsLineStringZmFromGeoPackageBinary()
    {
        var path = await CreateFixtureAsync();
        try
        {
            var feature = Assert.Single(await ReadAllAsync(path, "routes"));
            var line = Assert.IsType<LineStringGeometry>(feature.Geometry);

            Assert.Equal(4, line.Coordinates.Count);
            Assert.Equal(new GisCoordinate(0, 0, 1, 11), line.Coordinates[0]);
            Assert.Equal(new GisCoordinate(11, 11, 4, 14), line.Coordinates[^1]);
            Assert.Equal("route", feature.Attributes["name"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsSqliteFileWithoutGeoPackageApplicationId()
    {
        var path = await CreateFixtureAsync();
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA application_id = 0;";
                await command.ExecuteNonQueryAsync();
            }

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                async () => await _reader.ReadMetadataAsync(path));
            Assert.Contains("application_id", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
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

    private static async Task<string> CreateFixtureAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SpatialViewer-GisCore-{Guid.NewGuid():N}.gpkg");
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        await ExecuteAsync(
            connection,
            """
            PRAGMA application_id = 1196444487;
            CREATE TABLE gpkg_spatial_ref_sys (
              srs_name TEXT NOT NULL,
              srs_id INTEGER NOT NULL PRIMARY KEY,
              organization TEXT NOT NULL,
              organization_coordsys_id INTEGER NOT NULL,
              definition TEXT NOT NULL,
              description TEXT
            );
            CREATE TABLE gpkg_contents (
              table_name TEXT NOT NULL PRIMARY KEY,
              data_type TEXT NOT NULL,
              identifier TEXT UNIQUE,
              description TEXT DEFAULT '',
              last_change DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
              min_x DOUBLE,
              min_y DOUBLE,
              max_x DOUBLE,
              max_y DOUBLE,
              srs_id INTEGER
            );
            CREATE TABLE gpkg_geometry_columns (
              table_name TEXT NOT NULL,
              column_name TEXT NOT NULL,
              geometry_type_name TEXT NOT NULL,
              srs_id INTEGER NOT NULL,
              z TINYINT NOT NULL,
              m TINYINT NOT NULL,
              PRIMARY KEY (table_name, column_name)
            );
            CREATE TABLE places (
              fid INTEGER PRIMARY KEY AUTOINCREMENT,
              geom BLOB,
              name TEXT,
              score REAL,
              nullable TEXT
            );
            CREATE TABLE routes (
              fid INTEGER PRIMARY KEY,
              geom BLOB NOT NULL,
              name TEXT
            );
            CREATE VIRTUAL TABLE rtree_places_geom USING rtree(id, minx, maxx, miny, maxy);
            """);

        await ExecuteAsync(
            connection,
            """
            INSERT INTO gpkg_spatial_ref_sys
              (srs_name, srs_id, organization, organization_coordsys_id, definition, description)
            VALUES
              ('WGS 84', 4326, 'EPSG', 4326,
               'GEOGCS["WGS 84",DATUM["WGS_1984"],UNIT["degree",0.0174532925199433],AUTHORITY["EPSG","4326"]]',
               'test');
            INSERT INTO gpkg_contents
              (table_name, data_type, identifier, min_x, min_y, max_x, max_y, srs_id)
            VALUES
              ('places', 'features', 'places', 1, 2, 100, 100, 4326),
              ('routes', 'features', 'routes', 0, 0, 11, 11, 4326);
            INSERT INTO gpkg_geometry_columns
              (table_name, column_name, geometry_type_name, srs_id, z, m)
            VALUES
              ('places', 'geom', 'POINT', 4326, 1, 1),
              ('routes', 'geom', 'LINESTRING', 4326, 1, 1);
            """);

        await InsertPlaceAsync(connection, CreatePointBlob(1, 2, 3, 4), "東京", 12.5, null);
        await InsertPlaceAsync(connection, CreatePointBlob(100, 100, 5, 6), "outside", -2, "x");
        await InsertPlaceAsync(connection, null, "null", 0, null);
        await ExecuteAsync(
            connection,
            """
            INSERT INTO rtree_places_geom VALUES (1, 1, 1, 2, 2);
            INSERT INTO rtree_places_geom VALUES (2, 100, 100, 100, 100);
            """);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO routes(fid, geom, name) VALUES (1, $geom, 'route');";
            command.Parameters.AddWithValue(
                "$geom",
                CreateLineStringBlob(
                    new[]
                    {
                        new GisCoordinate(0, 0, 1, 11),
                        new GisCoordinate(1, 1, 2, 12),
                        new GisCoordinate(10, 10, 3, 13),
                        new GisCoordinate(11, 11, 4, 14),
                    }));
            await command.ExecuteNonQueryAsync();
        }

        return path;
    }

    private static async Task InsertPlaceAsync(
        SqliteConnection connection,
        byte[]? geometry,
        string name,
        double score,
        string? nullable)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO places(geom, name, score, nullable) VALUES ($geom, $name, $score, $nullable);";
        command.Parameters.AddWithValue("$geom", geometry is null ? DBNull.Value : geometry);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$score", score);
        command.Parameters.AddWithValue("$nullable", nullable is null ? DBNull.Value : nullable);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static byte[] CreatePointBlob(double x, double y, double z, double m)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteGeoPackageHeader(writer, new[] { new GisCoordinate(x, y, z, m) });
        writer.Write((byte)1);
        writer.Write(3001u);
        WriteCoordinate(writer, new GisCoordinate(x, y, z, m));
        return stream.ToArray();
    }

    private static byte[] CreateLineStringBlob(GisCoordinate[] coordinates)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteGeoPackageHeader(writer, coordinates);
        writer.Write((byte)1);
        writer.Write(3002u);
        writer.Write(checked((uint)coordinates.Length));

        foreach (var coordinate in coordinates)
        {
            WriteCoordinate(writer, coordinate);
        }

        return stream.ToArray();
    }

    private static void WriteGeoPackageHeader(
        BinaryWriter writer,
        GisCoordinate[] coordinates)
    {
        writer.Write((byte)'G');
        writer.Write((byte)'P');
        writer.Write((byte)0);
        writer.Write((byte)9);
        writer.Write(4326);

        writer.Write(coordinates.Min(coordinate => coordinate.X));
        writer.Write(coordinates.Max(coordinate => coordinate.X));
        writer.Write(coordinates.Min(coordinate => coordinate.Y));
        writer.Write(coordinates.Max(coordinate => coordinate.Y));
        writer.Write(coordinates.Min(coordinate => coordinate.Z ?? 0));
        writer.Write(coordinates.Max(coordinate => coordinate.Z ?? 0));
        writer.Write(coordinates.Min(coordinate => coordinate.M ?? 0));
        writer.Write(coordinates.Max(coordinate => coordinate.M ?? 0));
    }

    private static void WriteCoordinate(BinaryWriter writer, GisCoordinate coordinate)
    {
        writer.Write(coordinate.X);
        writer.Write(coordinate.Y);
        writer.Write(coordinate.Z ?? double.NaN);
        writer.Write(coordinate.M ?? double.NaN);
    }
}
