using System.Collections.Concurrent;
using System.Globalization;
using SpatialViewer.Formats.Gis;
using SpatialViewer.Gis.Core;
using SpatialViewer.Gis.Projections;
using StbImageSharp;

namespace SpatialViewer.Formats.Gis.WorldImage;

public sealed class WorldImageDataSourceReader : IRasterDataSourceReader
{
    private readonly ConcurrentDictionary<string, WeakReference<DecodedImage>> _decodedCache =
        new(StringComparer.OrdinalIgnoreCase);

    public string FormatId => "world-image";

    public ValueTask<GisDatasetMetadata> ReadMetadataAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        var info = ImageInfo.FromStream(stream)
            ?? throw new InvalidDataException($"Unsupported or invalid image file '{path}'.");
        var geoTransform = ReadWorldFile(path);
        var spatialReference = ReadPrj(path);
        var bounds = geoTransform?.GetBounds(info.Width, info.Height);
        var bandCount = GetBandCount(info.ColorComponents);
        var bands = CreateBands(info.ColorComponents, info.BitsPerChannel);

        var layer = new RasterLayerMetadata(
            "raster",
            spatialReference,
            bounds,
            info.Width,
            info.Height,
            bandCount)
        {
            GeoTransform = geoTransform,
            PixelAnchor = RasterPixelAnchor.Area,
            Bands = bands,
            Overviews = Array.Empty<RasterOverviewMetadata>(),
            ColorModel = GetColorModel(info.ColorComponents),
            IsTiled = false,
        };

        return ValueTask.FromResult(
            new GisDatasetMetadata(
                Path.GetFileName(path),
                FormatId,
                new GisLayerMetadata[] { layer }));
    }

    public ValueTask<RasterReadResult> ReadRasterAsync(
        string path,
        string layerName,
        RasterReadRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(layerName, "raster", StringComparison.Ordinal))
        {
            throw new ArgumentException($"World-image layer '{layerName}' does not exist. Expected 'raster'.", nameof(layerName));
        }

        var image = GetDecodedImage(path, cancellationToken);
        var clippedWindow = request.Window.Intersect(image.Width, image.Height)
            ?? throw new ArgumentOutOfRangeException(nameof(request), "Raster read window does not intersect the image.");
        var output = ReadWindowNearest(
            image,
            clippedWindow,
            request.OutputWidth,
            request.OutputHeight,
            cancellationToken);
        return ValueTask.FromResult(
            new RasterReadResult(
                request.OutputWidth,
                request.OutputHeight,
                RasterPixelFormat.Rgba32,
                output,
                0,
                clippedWindow));
    }

    private DecodedImage GetDecodedImage(string path, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        if (_decodedCache.TryGetValue(fullPath, out var weak) && weak.TryGetTarget(out var cached))
        {
            return cached;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(fullPath);
        var result = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        cancellationToken.ThrowIfCancellationRequested();
        var decoded = new DecodedImage(result.Width, result.Height, result.Data);
        _decodedCache[fullPath] = new WeakReference<DecodedImage>(decoded);
        return decoded;
    }

    private static byte[] ReadWindowNearest(
        DecodedImage image,
        RasterWindow window,
        int outputWidth,
        int outputHeight,
        CancellationToken cancellationToken)
    {
        var output = new byte[checked(outputWidth * outputHeight * 4)];
        for (var y = 0; y < outputHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceY = window.Y + Math.Min(
                window.Height - 1,
                ((2 * y + 1) * window.Height) / (2 * outputHeight));

            for (var x = 0; x < outputWidth; x++)
            {
                var sourceX = window.X + Math.Min(
                    window.Width - 1,
                    ((2 * x + 1) * window.Width) / (2 * outputWidth));
                var sourceOffset = ((sourceY * image.Width) + sourceX) * 4;
                var destinationOffset = ((y * outputWidth) + x) * 4;
                Buffer.BlockCopy(image.Pixels, sourceOffset, output, destinationOffset, 4);
            }
        }

        return output;
    }

    private static RasterGeoTransform? ReadWorldFile(string imagePath)
    {
        var worldFile = FindWorldFile(imagePath);
        if (worldFile is null)
        {
            return null;
        }

        var lines = File.ReadAllLines(worldFile)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        if (lines.Length != 6)
        {
            throw new InvalidDataException($"World file '{worldFile}' must contain exactly six numeric values.");
        }

        var values = new double[6];
        for (var index = 0; index < values.Length; index++)
        {
            if (!double.TryParse(lines[index], NumberStyles.Float, CultureInfo.InvariantCulture, out values[index]) ||
                !double.IsFinite(values[index]))
            {
                throw new InvalidDataException($"World file '{worldFile}' contains an invalid value on line {index + 1}.");
            }
        }

        var pixelWidth = values[0];
        var columnRotation = values[1];
        var rowRotation = values[2];
        var pixelHeight = values[3];
        var centerX = values[4];
        var centerY = values[5];
        return new RasterGeoTransform(
            centerX - ((pixelWidth + rowRotation) / 2d),
            pixelWidth,
            rowRotation,
            centerY - ((columnRotation + pixelHeight) / 2d),
            columnRotation,
            pixelHeight);
    }

    private static string? FindWorldFile(string imagePath)
    {
        var directory = Path.GetDirectoryName(imagePath) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(imagePath);
        var extension = Path.GetExtension(imagePath).ToLowerInvariant();
        var candidateExtensions = extension switch
        {
            ".png" => new[] { ".pgw", ".pngw", ".wld" },
            ".jpg" => new[] { ".jgw", ".jpgw", ".wld" },
            ".jpeg" => new[] { ".jgw", ".jpegw", ".wld" },
            _ => Array.Empty<string>(),
        };

        foreach (var candidateExtension in candidateExtensions)
        {
            var candidate = Path.Combine(directory, fileNameWithoutExtension + candidateExtension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static SpatialReference ReadPrj(string imagePath)
    {
        var prjPath = Path.ChangeExtension(imagePath, ".prj");
        if (!File.Exists(prjPath))
        {
            return SpatialReference.Unknown;
        }

        var wkt = File.ReadAllText(prjPath).Trim();
        return string.IsNullOrWhiteSpace(wkt)
            ? SpatialReference.Unknown
            : SpatialReferenceParser.ParseWkt(wkt);
    }

    private static RasterBandMetadata[] CreateBands(ColorComponents components, int bitsPerChannel)
    {
        var count = GetBandCount(components);
        var result = new RasterBandMetadata[count];
        for (var index = 0; index < count; index++)
        {
            result[index] = new RasterBandMetadata(
                index + 1,
                RasterSampleType.UnsignedInteger,
                bitsPerChannel,
                GetInterpretation(components, index));
        }

        return result;
    }

    private static int GetBandCount(ColorComponents components) => components switch
    {
        ColorComponents.Grey => 1,
        ColorComponents.GreyAlpha => 2,
        ColorComponents.RedGreenBlue => 3,
        ColorComponents.RedGreenBlueAlpha => 4,
        _ => 4,
    };

    private static RasterColorInterpretation GetInterpretation(ColorComponents components, int index) =>
        components switch
        {
            ColorComponents.Grey => RasterColorInterpretation.Gray,
            ColorComponents.GreyAlpha => index == 0
                ? RasterColorInterpretation.Gray
                : RasterColorInterpretation.Alpha,
            ColorComponents.RedGreenBlue or ColorComponents.RedGreenBlueAlpha => index switch
            {
                0 => RasterColorInterpretation.Red,
                1 => RasterColorInterpretation.Green,
                2 => RasterColorInterpretation.Blue,
                3 => RasterColorInterpretation.Alpha,
                _ => RasterColorInterpretation.Unknown,
            },
            _ => RasterColorInterpretation.Unknown,
        };

    private static string GetColorModel(ColorComponents components) => components switch
    {
        ColorComponents.Grey => "Gray",
        ColorComponents.GreyAlpha => "GrayAlpha",
        ColorComponents.RedGreenBlue => "RGB",
        ColorComponents.RedGreenBlueAlpha => "RGBA",
        _ => "Unknown",
    };

    private sealed record DecodedImage(int Width, int Height, byte[] Pixels);
}
