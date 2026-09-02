namespace SpatialViewer.Gis.Core;

public sealed class MapImageRequest
{
    public MapImageRequest(
        Envelope2D bounds,
        int width,
        int height,
        SpatialReference spatialReference,
        TileContentType contentType = TileContentType.Png)
    {
        if (!bounds.IsValid)
        {
            throw new ArgumentException("Map image bounds must be valid.", nameof(bounds));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(spatialReference);
        if (spatialReference.IsUnknown)
        {
            throw new ArgumentException("Map image requests require an explicit spatial reference.", nameof(spatialReference));
        }

        if (contentType is not (TileContentType.Png or TileContentType.Jpeg or TileContentType.WebP))
        {
            throw new ArgumentException("Map image content type must be a raster image format.", nameof(contentType));
        }

        Bounds = bounds;
        Width = width;
        Height = height;
        SpatialReference = spatialReference;
        ContentType = contentType;
    }

    public Envelope2D Bounds { get; }

    public int Width { get; }

    public int Height { get; }

    public SpatialReference SpatialReference { get; }

    public TileContentType ContentType { get; }
}

public sealed class MapImageResult
{
    public MapImageResult(
        MapImageRequest request,
        TileContentType contentType,
        ReadOnlyMemory<byte> content)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (contentType is not (TileContentType.Png or TileContentType.Jpeg or TileContentType.WebP))
        {
            throw new ArgumentException("Map image content type must be a raster image format.", nameof(contentType));
        }

        if (content.IsEmpty)
        {
            throw new ArgumentException("Map image content must not be empty.", nameof(content));
        }

        Request = request;
        ContentType = contentType;
        Content = content;
    }

    public MapImageRequest Request { get; }

    public TileContentType ContentType { get; }

    public ReadOnlyMemory<byte> Content { get; }

    public string? EntityTag { get; init; }

    public DateTimeOffset? LastModified { get; init; }

    public int ByteLength => Content.Length;
}
