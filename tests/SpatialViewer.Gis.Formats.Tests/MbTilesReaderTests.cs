using Microsoft.Data.Sqlite;
using SpatialViewer.Formats.Gis.MbTiles;
using SpatialViewer.Gis.Core;
using Xunit;

namespace SpatialViewer.Gis.Formats.Tests;

public sealed class MbTilesReaderTests
{
    private readonly MbTilesDataSourceReader _reader = new();

    [Theory]
    [InlineData("sample.mbtiles", true)]
    [InlineData("sample.MBTILES", true)]
    [InlineData("sample.sqlite", false)]
    public async Task ProbeUsesMbTilesExtension(string path, bool expected)
    {
        var result = await new MbTilesFormatProbe().ProbeAsync(path);
        Assert.Equal(expected, result.IsMatch);
    }

    [Fact]
    public async Task ReadsMetadataAndConvertsCanonicalXyzToStoredTmsRow()
    {
        var path = CreateTemporaryPath();
        try
        {
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            await CreateMbTilesAsync(path, "png", 1, 1, 0, 1, png);

            var metadata = await _reader.ReadMetadataAsync(path);
            Assert.Equal("Synthetic tiles", metadata.Name);
            Assert.Equal(TileScheme.Tms, metadata.StorageScheme);
            Assert.Equal(1, metadata.MinimumZoom);
            Assert.Equal(1, metadata.MaximumZoom);
            Assert.Equal(256, metadata.TileSize);
            Assert.Equal(SpatialReference.FromEpsg(3857), metadata.SpatialReference);
            Assert.Equal(TileContentType.Png, metadata.ContentType);
            Assert.Equal(new Envelope2D(-10, -5, 10, 5), metadata.GeographicBounds);
            Assert.Equal("Synthetic attribution", metadata.Attribution);

            var storedTile = await _reader.ReadTileAsync(path, "tiles", new TileCoordinate(1, 1, 1));
            var missingNorthTile = await _reader.ReadTileAsync(path, "tiles", new TileCoordinate(1, 1, 0));

            Assert.NotNull(storedTile);
            Assert.Equal(TileContentType.Png, storedTile.ContentType);
            Assert.Equal(png, storedTile.Content.ToArray());
            Assert.Null(missingNorthTile);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task VectorPbfMetadataKeepsEncodedPayloadWithoutRasterDecoding()
    {
        var path = CreateTemporaryPath();
        try
        {
            var payload = new byte[] { 0x1A, 0x02, 0x08, 0x01 };
            await CreateMbTilesAsync(path, "pbf", 0, 0, 0, 0, payload);

            var tile = await _reader.ReadTileAsync(path, "tiles", new TileCoordinate(0, 0, 0));

            Assert.NotNull(tile);
            Assert.Equal(TileContentType.VectorPbf, tile.ContentType);
            Assert.Equal(TilePayloadKind.VectorTile, tile.PayloadKind);
            Assert.Equal(payload, tile.Content.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsPlainSqliteWithoutRequiredMbTilesTables()
    {
        var path = CreateTemporaryPath();
        try
        {
            await using (var connection = await OpenWritableAsync(path))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE unrelated(id INTEGER PRIMARY KEY);";
                await command.ExecuteNonQueryAsync();
            }

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                async () => await _reader.ReadMetadataAsync(path));
            Assert.Contains("metadata and tiles", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTemporaryPath() => Path.Combine(
        Path.GetTempPath(),
        $"SpatialViewer-GisCore-{Guid.NewGuid():N}.mbtiles");

    private static async Task CreateMbTilesAsync(
        string path,
        string format,
        int zoom,
        int column,
        int tmsRow,
        int minimumZoom,
        byte[] payload)
    {
        await using var connection = await OpenWritableAsync(path);
        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText =
                """
                CREATE TABLE metadata(name TEXT NOT NULL, value TEXT NOT NULL);
                CREATE TABLE tiles(
                    zoom_level INTEGER NOT NULL,
                    tile_column INTEGER NOT NULL,
                    tile_row INTEGER NOT NULL,
                    tile_data BLOB NOT NULL);
                CREATE UNIQUE INDEX tile_index ON tiles(zoom_level, tile_column, tile_row);
                """;
            await schema.ExecuteNonQueryAsync();
        }

        await using (var metadata = connection.CreateCommand())
        {
            metadata.CommandText =
                """
                INSERT INTO metadata(name, value) VALUES
                    ('name', 'Synthetic tiles'),
                    ('format', $format),
                    ('minzoom', $minimumZoom),
                    ('maxzoom', $zoom),
                    ('bounds', '-10,-5,10,5'),
                    ('attribution', 'Synthetic attribution');
                """;
            metadata.Parameters.AddWithValue("$format", format);
            metadata.Parameters.AddWithValue("$minimumZoom", minimumZoom.ToString(System.Globalization.CultureInfo.InvariantCulture));
            metadata.Parameters.AddWithValue("$zoom", zoom.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await metadata.ExecuteNonQueryAsync();
        }

        await using var tile = connection.CreateCommand();
        tile.CommandText =
            """
            INSERT INTO tiles(zoom_level, tile_column, tile_row, tile_data)
            VALUES ($zoom, $column, $row, $data);
            """;
        tile.Parameters.AddWithValue("$zoom", zoom);
        tile.Parameters.AddWithValue("$column", column);
        tile.Parameters.AddWithValue("$row", tmsRow);
        tile.Parameters.AddWithValue("$data", payload);
        await tile.ExecuteNonQueryAsync();
    }

    private static async Task<SqliteConnection> OpenWritableAsync(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }
}
