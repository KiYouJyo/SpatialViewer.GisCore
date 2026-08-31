namespace SpatialViewer.Gis.Core;

public enum RasterSampleType
{
    Unknown,
    UnsignedInteger,
    SignedInteger,
    FloatingPoint,
}

public enum RasterColorInterpretation
{
    Unknown,
    Gray,
    Red,
    Green,
    Blue,
    Alpha,
    Palette,
}

public enum RasterPixelFormat
{
    Gray8,
    GrayAlpha8,
    Rgb24,
    Rgba32,
}

public enum RasterPixelAnchor
{
    Area,
    Point,
}

public readonly record struct RasterWindow(int X, int Y, int Width, int Height)
{
    public bool IsValid => X >= 0 && Y >= 0 && Width > 0 && Height > 0;

    public int Right => checked(X + Width);

    public int Bottom => checked(Y + Height);

    public RasterWindow? Intersect(int rasterWidth, int rasterHeight)
    {
        if (rasterWidth <= 0 || rasterHeight <= 0 || !IsValid)
        {
            return null;
        }

        var left = Math.Max(0, X);
        var top = Math.Max(0, Y);
        var right = Math.Min(rasterWidth, Right);
        var bottom = Math.Min(rasterHeight, Bottom);
        return right <= left || bottom <= top
            ? null
            : new RasterWindow(left, top, right - left, bottom - top);
    }
}

public readonly record struct RasterGeoTransform(
    double OriginX,
    double PixelWidth,
    double RowRotation,
    double OriginY,
    double ColumnRotation,
    double PixelHeight)
{
    public GisCoordinate PixelToWorld(double column, double row) => new(
        OriginX + (column * PixelWidth) + (row * RowRotation),
        OriginY + (column * ColumnRotation) + (row * PixelHeight));

    public bool TryWorldToPixel(double x, double y, out double column, out double row)
    {
        var determinant = (PixelWidth * PixelHeight) - (RowRotation * ColumnRotation);
        if (!double.IsFinite(determinant) || Math.Abs(determinant) <= double.Epsilon)
        {
            column = default;
            row = default;
            return false;
        }

        var deltaX = x - OriginX;
        var deltaY = y - OriginY;
        column = ((deltaX * PixelHeight) - (RowRotation * deltaY)) / determinant;
        row = ((PixelWidth * deltaY) - (deltaX * ColumnRotation)) / determinant;
        return double.IsFinite(column) && double.IsFinite(row);
    }

    public Envelope2D GetBounds(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        var topLeft = PixelToWorld(0, 0);
        var topRight = PixelToWorld(width, 0);
        var bottomLeft = PixelToWorld(0, height);
        var bottomRight = PixelToWorld(width, height);
        return new Envelope2D(
            Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomLeft.X, bottomRight.X)),
            Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomLeft.Y, bottomRight.Y)),
            Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomLeft.X, bottomRight.X)),
            Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomLeft.Y, bottomRight.Y)));
    }
}

public sealed record RasterBandMetadata(
    int BandIndex,
    RasterSampleType SampleType,
    int BitsPerSample,
    RasterColorInterpretation ColorInterpretation,
    double? NoDataValue = null);

public sealed record RasterOverviewMetadata(
    int Level,
    int Width,
    int Height,
    double DecimationX,
    double DecimationY);

public sealed class RasterReadRequest
{
    public RasterReadRequest(RasterWindow window, int outputWidth, int outputHeight)
    {
        if (!window.IsValid)
        {
            throw new ArgumentException("Raster read window must be valid.", nameof(window));
        }

        if (outputWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputWidth));
        }

        if (outputHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputHeight));
        }

        Window = window;
        OutputWidth = outputWidth;
        OutputHeight = outputHeight;
    }

    public RasterWindow Window { get; }

    public int OutputWidth { get; }

    public int OutputHeight { get; }
}

public sealed class RasterReadResult
{
    public RasterReadResult(
        int width,
        int height,
        RasterPixelFormat pixelFormat,
        ReadOnlyMemory<byte> pixels,
        int overviewLevel,
        RasterWindow sourceWindow)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        var expectedLength = checked(width * height * GetBytesPerPixel(pixelFormat));
        if (pixels.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Raster pixel buffer length {pixels.Length} does not match expected length {expectedLength}.",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        PixelFormat = pixelFormat;
        Pixels = pixels;
        OverviewLevel = overviewLevel;
        SourceWindow = sourceWindow;
    }

    public int Width { get; }

    public int Height { get; }

    public RasterPixelFormat PixelFormat { get; }

    public ReadOnlyMemory<byte> Pixels { get; }

    public int OverviewLevel { get; }

    public RasterWindow SourceWindow { get; }

    public int ByteLength => Pixels.Length;

    public static int GetBytesPerPixel(RasterPixelFormat pixelFormat) => pixelFormat switch
    {
        RasterPixelFormat.Gray8 => 1,
        RasterPixelFormat.GrayAlpha8 => 2,
        RasterPixelFormat.Rgb24 => 3,
        RasterPixelFormat.Rgba32 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat)),
    };
}
