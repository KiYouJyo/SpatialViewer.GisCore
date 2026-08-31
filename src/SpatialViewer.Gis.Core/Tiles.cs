namespace SpatialViewer.Gis.Core;

public enum TileScheme
{
    Xyz,
    Tms,
}

public enum TileContentType
{
    Unknown,
    Png,
    Jpeg,
    WebP,
    VectorPbf,
}

public enum TilePayloadKind
{
    Unknown,
    RasterImage,
    VectorTile,
}

public readonly record struct TileCoordinate(int Zoom, int X, int Y)
{
    public bool IsValid
    {
        get
        {
            if (Zoom is < 0 or > 30)
            {
                return false;
            }

            var matrixSize = 1 << Zoom;
            return X >= 0 && Y >= 0 && X < matrixSize && Y < matrixSize;
        }
    }

    public int MatrixSize
    {
        get
        {
            if (Zoom is < 0 or > 30)
            {
                throw new ArgumentOutOfRangeException(nameof(Zoom), "Tile zoom must be between 0 and 30.");
            }

            return 1 << Zoom;
        }
    }

    public int ToTmsRow()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("Only a valid XYZ tile coordinate can be converted to a TMS row.");
        }

        return MatrixSize - 1 - Y;
    }

    public static TileCoordinate FromTmsRow(int zoom, int x, int tmsRow)
    {
        if (zoom is < 0 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom), "Tile zoom must be between 0 and 30.");
        }

        var matrixSize = 1 << zoom;
        if (x < 0 || x >= matrixSize)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (tmsRow < 0 || tmsRow >= matrixSize)
        {
            throw new ArgumentOutOfRangeException(nameof(tmsRow));
        }

        return new TileCoordinate(zoom, x, matrixSize - 1 - tmsRow);
    }
}

public sealed class TileSourceMetadata
{
    public TileSourceMetadata(
        string name,
        TileScheme storageScheme,
        int minimumZoom,
        int maximumZoom,
        int tileSize,
        SpatialReference spatialReference,
        TileContentType contentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumZoom);
        if (maximumZoom < minimumZoom || maximumZoom > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumZoom));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileSize);
        ArgumentNullException.ThrowIfNull(spatialReference);

        Name = name;
        StorageScheme = storageScheme;
        MinimumZoom = minimumZoom;
        MaximumZoom = maximumZoom;
        TileSize = tileSize;
        SpatialReference = spatialReference;
        ContentType = contentType;
    }

    public string Name { get; }

    public TileScheme StorageScheme { get; }

    public int MinimumZoom { get; }

    public int MaximumZoom { get; }

    public int TileSize { get; }

    public SpatialReference SpatialReference { get; }

    public TileContentType ContentType { get; }

    public Envelope2D? GeographicBounds { get; init; }

    public string? Attribution { get; init; }
}

public sealed class TileReadResult
{
    public TileReadResult(
        TileCoordinate coordinate,
        TileContentType contentType,
        ReadOnlyMemory<byte> content)
    {
        if (!coordinate.IsValid)
        {
            throw new ArgumentException("Tile coordinate must be valid.", nameof(coordinate));
        }

        if (content.IsEmpty)
        {
            throw new ArgumentException("Tile content must not be empty.", nameof(content));
        }

        Coordinate = coordinate;
        ContentType = contentType;
        Content = content;
    }

    public TileCoordinate Coordinate { get; }

    public TileContentType ContentType { get; }

    public ReadOnlyMemory<byte> Content { get; }

    public string? EntityTag { get; init; }

    public DateTimeOffset? LastModified { get; init; }

    public int ByteLength => Content.Length;

    public TilePayloadKind PayloadKind => ContentType switch
    {
        TileContentType.Png or TileContentType.Jpeg or TileContentType.WebP => TilePayloadKind.RasterImage,
        TileContentType.VectorPbf => TilePayloadKind.VectorTile,
        _ => TilePayloadKind.Unknown,
    };
}

public static class WebMercatorTileMath
{
    public const double MaximumCoordinate = 20037508.342789244;

    public static Envelope2D GetBounds(TileCoordinate coordinate)
    {
        if (!coordinate.IsValid)
        {
            throw new ArgumentException("Tile coordinate must be valid.", nameof(coordinate));
        }

        var matrixSize = coordinate.MatrixSize;
        var span = (MaximumCoordinate * 2d) / matrixSize;
        var minX = -MaximumCoordinate + (coordinate.X * span);
        var maxX = minX + span;
        var maxY = MaximumCoordinate - (coordinate.Y * span);
        var minY = maxY - span;
        return new Envelope2D(minX, minY, maxX, maxY);
    }

    public static double Resolution(int zoom, int tileSize = 256)
    {
        if (zoom is < 0 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileSize);
        return (MaximumCoordinate * 2d) / ((1L << zoom) * tileSize);
    }
}
