using System.Globalization;
using Microsoft.Data.Sqlite;
using SpatialViewer.Formats.Gis;
using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis.MbTiles;

public sealed class MbTilesDataSourceReader : ITileDataSourceReader
{
    public string FormatId => MbTilesFormatProbe.FormatId;

    public async ValueTask<TileSourceMetadata> ReadMetadataAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        var path = ValidatePath(source);
        await using var connection = await OpenConnectionAsync(path, cancellationToken).ConfigureAwait(false);
        await ValidateSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        var metadata = await ReadMetadataDictionaryAsync(connection, cancellationToken).ConfigureAwait(false);
        var zoomRange = await ReadZoomRangeAsync(connection, metadata, cancellationToken).ConfigureAwait(false);
        var tileSize = ReadTileSize(metadata);
        var contentType = ParseContentType(GetMetadataValue(metadata, "format"));
        var name = GetMetadataValue(metadata, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileNameWithoutExtension(path);
        }

        return new TileSourceMetadata(
            name,
            TileScheme.Tms,
            zoomRange.Minimum,
            zoomRange.Maximum,
            tileSize,
            SpatialReference.FromEpsg(3857),
            contentType)
        {
            GeographicBounds = ParseGeographicBounds(GetMetadataValue(metadata, "bounds")),
            Attribution = GetMetadataValue(metadata, "attribution"),
        };
    }

    public async ValueTask<TileReadResult?> ReadTileAsync(
        string source,
        string layerName,
        TileCoordinate coordinate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
        if (!string.Equals(layerName, "tiles", StringComparison.Ordinal))
        {
            throw new ArgumentException($"MBTiles layer '{layerName}' does not exist. Expected 'tiles'.", nameof(layerName));
        }

        if (!coordinate.IsValid)
        {
            throw new ArgumentException("Tile coordinate must be valid.", nameof(coordinate));
        }

        var path = ValidatePath(source);
        await using var connection = await OpenConnectionAsync(path, cancellationToken).ConfigureAwait(false);
        await ValidateSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT tile_data
              FROM tiles
             WHERE zoom_level = $zoom
               AND tile_column = $column
               AND tile_row = $row
             LIMIT 1;
            """;
        command.Parameters.AddWithValue("$zoom", coordinate.Zoom);
        command.Parameters.AddWithValue("$column", coordinate.X);
        command.Parameters.AddWithValue("$row", coordinate.ToTmsRow());
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null or DBNull)
        {
            return null;
        }

        if (value is not byte[] content || content.Length == 0)
        {
            throw new InvalidDataException("MBTiles tile_data must be a non-empty BLOB.");
        }

        var declaredType = await ReadDeclaredContentTypeAsync(connection, cancellationToken).ConfigureAwait(false);
        var contentType = declaredType == TileContentType.Unknown
            ? DetectContentType(content)
            : declaredType;
        return new TileReadResult(coordinate, contentType, content);
    }

    private static string ValidatePath(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var path = Path.GetFullPath(source);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("MBTiles file was not found.", path);
        }

        if (!string.Equals(Path.GetExtension(path), ".mbtiles", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("MBTiles source must use the .mbtiles extension.", nameof(source));
        }

        return path;
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

    private static async ValueTask ValidateSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
              FROM sqlite_master
             WHERE type = 'table'
               AND name IN ('metadata', 'tiles');
            """;
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var count = value is null or DBNull
            ? 0L
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        if (count != 2)
        {
            throw new InvalidDataException("SQLite file is not a conforming MBTiles dataset: required metadata and tiles tables were not found.");
        }
    }

    private static async ValueTask<Dictionary<string, string>> ReadMetadataDictionaryAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, value FROM metadata;";
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
            {
                continue;
            }

            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }

    private static async ValueTask<(int Minimum, int Maximum)> ReadZoomRangeAsync(
        SqliteConnection connection,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        if (TryReadZoom(metadata, "minzoom", out var minimum) &&
            TryReadZoom(metadata, "maxzoom", out var maximum) &&
            minimum <= maximum)
        {
            return (minimum, maximum);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MIN(zoom_level), MAX(zoom_level) FROM tiles;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.IsDBNull(0) || reader.IsDBNull(1))
        {
            throw new InvalidDataException("MBTiles dataset contains no readable tile zoom levels.");
        }

        minimum = reader.GetInt32(0);
        maximum = reader.GetInt32(1);
        if (minimum is < 0 or > 30 || maximum is < 0 or > 30 || minimum > maximum)
        {
            throw new InvalidDataException($"MBTiles zoom range {minimum}..{maximum} is outside the supported 0..30 range.");
        }

        return (minimum, maximum);
    }

    private static bool TryReadZoom(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        out int value)
    {
        var text = GetMetadataValue(metadata, key);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
            value is >= 0 and <= 30;
    }

    private static int ReadTileSize(IReadOnlyDictionary<string, string> metadata)
    {
        var text = GetMetadataValue(metadata, "tile_size");
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tileSize) && tileSize > 0
            ? tileSize
            : 256;
    }

    private static Envelope2D? ParseGeographicBounds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var west) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var south) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var east) ||
            !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var north) ||
            !double.IsFinite(west) || !double.IsFinite(south) || !double.IsFinite(east) || !double.IsFinite(north) ||
            west < -180 || east > 180 || south < -90 || north > 90 || west > east || south > north)
        {
            throw new InvalidDataException($"MBTiles bounds metadata '{text}' is invalid.");
        }

        return new Envelope2D(west, south, east, north);
    }

    private static async ValueTask<TileContentType> ReadDeclaredContentTypeAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM metadata WHERE name = 'format' LIMIT 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string text ? ParseContentType(text) : TileContentType.Unknown;
    }

    private static TileContentType ParseContentType(string? format) => format?.Trim().ToLowerInvariant() switch
    {
        "png" => TileContentType.Png,
        "jpg" or "jpeg" => TileContentType.Jpeg,
        "webp" => TileContentType.WebP,
        "pbf" or "mvt" => TileContentType.VectorPbf,
        _ => TileContentType.Unknown,
    };

    private static TileContentType DetectContentType(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 8 &&
            content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47)
        {
            return TileContentType.Png;
        }

        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return TileContentType.Jpeg;
        }

        if (content.Length >= 12 &&
            content[0] == (byte)'R' && content[1] == (byte)'I' && content[2] == (byte)'F' && content[3] == (byte)'F' &&
            content[8] == (byte)'W' && content[9] == (byte)'E' && content[10] == (byte)'B' && content[11] == (byte)'P')
        {
            return TileContentType.WebP;
        }

        return TileContentType.Unknown;
    }

    private static string? GetMetadataValue(
        IReadOnlyDictionary<string, string> metadata,
        string key) => metadata.TryGetValue(key, out var value) ? value : null;
}
