using System.Globalization;
using BitMiracle.LibTiff.Classic;
using SpatialViewer.Formats.Gis;
using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis.GeoTiff;

public sealed class GeoTiffDataSourceReader : IRasterDataSourceReader
{
    private const int ModelPixelScaleTag = 33550;
    private const int ModelTiepointTag = 33922;
    private const int ModelTransformationTag = 34264;
    private const int GeoKeyDirectoryTag = 34735;
    private const int GeoAsciiParamsTag = 34737;
    private const int GdalNoDataTag = 42113;

    private const int RasterTypeGeoKey = 1025;
    private const int GeographicTypeGeoKey = 2048;
    private const int GeographicCitationGeoKey = 2049;
    private const int ProjectedTypeGeoKey = 3072;
    private const int ProjectedCitationGeoKey = 3073;
    private const int GeoAsciiTagLocation = 34737;
    private const int UserDefinedCode = 32767;

    public string FormatId => "geotiff";

    public ValueTask<GisDatasetMetadata> ReadMetadataAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var tiff = OpenTiff(path);
        var directories = ReadDirectories(tiff, cancellationToken);
        var baseDirectory = directories[0];
        SetDirectory(tiff, baseDirectory.DirectoryIndex);

        var spatialReference = ReadSpatialReference(tiff);
        var pixelAnchor = ReadPixelAnchor(tiff);
        var geoTransform = ReadGeoTransform(tiff, pixelAnchor);
        var noData = ReadNoData(tiff);
        var bands = ReadBands(tiff, baseDirectory.SamplesPerPixel, baseDirectory.BitsPerSample, noData);
        var bounds = geoTransform?.GetBounds(baseDirectory.Width, baseDirectory.Height);
        var overviews = BuildOverviewMetadata(directories);

        var layer = new RasterLayerMetadata(
            "raster",
            spatialReference,
            bounds,
            baseDirectory.Width,
            baseDirectory.Height,
            baseDirectory.SamplesPerPixel)
        {
            GeoTransform = geoTransform,
            PixelAnchor = pixelAnchor,
            Bands = bands,
            Overviews = overviews,
            ColorModel = GetColorModel(baseDirectory.Photometric),
            IsTiled = baseDirectory.IsTiled,
        };

        var metadata = new GisDatasetMetadata(
            Path.GetFileName(path),
            FormatId,
            new GisLayerMetadata[] { layer });
        return ValueTask.FromResult(metadata);
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
            throw new ArgumentException($"GeoTIFF layer '{layerName}' does not exist. Expected 'raster'.", nameof(layerName));
        }

        using var tiff = OpenTiff(path);
        var directories = ReadDirectories(tiff, cancellationToken);
        var baseDirectory = directories[0];
        var clippedWindow = request.Window.Intersect(baseDirectory.Width, baseDirectory.Height)
            ?? throw new ArgumentOutOfRangeException(nameof(request), "Raster read window does not intersect the image.");
        var overviews = BuildOverviewMetadata(directories);
        var selectedLevel = RasterOverviewSelector.SelectLevel(
            clippedWindow,
            request.OutputWidth,
            request.OutputHeight,
            overviews);
        var selectedDirectory = FindDirectory(directories, selectedLevel);
        SetDirectory(tiff, selectedDirectory.DirectoryIndex);
        EnsureSupportedOrientation(tiff);

        var overviewWindow = MapWindowToDirectory(
            clippedWindow,
            baseDirectory.Width,
            baseDirectory.Height,
            selectedDirectory.Width,
            selectedDirectory.Height);
        var sourcePixels = DecodeWindowRgba(tiff, selectedDirectory, overviewWindow, cancellationToken);
        var outputPixels = ResizeRgbaNearest(
            sourcePixels,
            overviewWindow.Width,
            overviewWindow.Height,
            request.OutputWidth,
            request.OutputHeight,
            cancellationToken);

        return ValueTask.FromResult(
            new RasterReadResult(
                request.OutputWidth,
                request.OutputHeight,
                RasterPixelFormat.Rgba32,
                outputPixels,
                selectedLevel,
                clippedWindow));
    }

    private static Tiff OpenTiff(string path) =>
        Tiff.Open(path, "r") ?? throw new InvalidDataException($"Unable to open TIFF file '{path}'.");

    private static List<TiffDirectoryDescriptor> ReadDirectories(
        Tiff tiff,
        CancellationToken cancellationToken)
    {
        var directories = new List<TiffDirectoryDescriptor>();
        short directoryIndex = 0;

        while (tiff.SetDirectory(directoryIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSupportedOrientation(tiff);
            var width = GetRequiredInt(tiff, TiffTag.IMAGEWIDTH);
            var height = GetRequiredInt(tiff, TiffTag.IMAGELENGTH);
            var samplesPerPixel = GetDefaultedInt(tiff, TiffTag.SAMPLESPERPIXEL);
            var bitsPerSample = GetDefaultedInt(tiff, TiffTag.BITSPERSAMPLE);
            var photometric = GetDefaultedInt(tiff, TiffTag.PHOTOMETRIC);
            directories.Add(new TiffDirectoryDescriptor(
                directoryIndex,
                width,
                height,
                samplesPerPixel,
                bitsPerSample,
                photometric,
                tiff.IsTiled()));

            if (directoryIndex == short.MaxValue)
            {
                break;
            }

            directoryIndex++;
        }

        if (directories.Count == 0)
        {
            throw new InvalidDataException("TIFF file contains no readable image directories.");
        }

        SetDirectory(tiff, 0);
        return directories;
    }

    private static RasterOverviewMetadata[] BuildOverviewMetadata(List<TiffDirectoryDescriptor> directories)
    {
        var baseDirectory = directories[0];
        var result = new List<RasterOverviewMetadata>();

        for (var index = 1; index < directories.Count; index++)
        {
            var directory = directories[index];
            if (directory.Width >= baseDirectory.Width || directory.Height >= baseDirectory.Height)
            {
                continue;
            }

            result.Add(new RasterOverviewMetadata(
                directory.DirectoryIndex,
                directory.Width,
                directory.Height,
                (double)baseDirectory.Width / directory.Width,
                (double)baseDirectory.Height / directory.Height));
        }

        return result.ToArray();
    }

    private static TiffDirectoryDescriptor FindDirectory(
        List<TiffDirectoryDescriptor> directories,
        int level)
    {
        if (level == 0)
        {
            return directories[0];
        }

        foreach (var directory in directories)
        {
            if (directory.DirectoryIndex == level)
            {
                return directory;
            }
        }

        throw new InvalidDataException($"TIFF overview directory {level} is no longer available.");
    }

    private static SpatialReference ReadSpatialReference(Tiff tiff)
    {
        var keys = GetShortArray(tiff, GeoKeyDirectoryTag);
        if (keys is null || keys.Length < 4)
        {
            return SpatialReference.Unknown;
        }

        var projected = GetGeoKeyShortValue(keys, ProjectedTypeGeoKey);
        if (projected is > 0 and not UserDefinedCode)
        {
            return SpatialReference.FromEpsg(projected.Value);
        }

        var geographic = GetGeoKeyShortValue(keys, GeographicTypeGeoKey);
        if (geographic is > 0 and not UserDefinedCode)
        {
            return SpatialReference.FromEpsg(geographic.Value);
        }

        var citation = ReadGeoCitation(tiff, keys, ProjectedCitationGeoKey) ??
            ReadGeoCitation(tiff, keys, GeographicCitationGeoKey);
        return citation is null
            ? SpatialReference.Unknown
            : new SpatialReference(null, null, null, citation);
    }

    private static RasterPixelAnchor ReadPixelAnchor(Tiff tiff)
    {
        var keys = GetShortArray(tiff, GeoKeyDirectoryTag);
        var rasterType = keys is null ? null : GetGeoKeyShortValue(keys, RasterTypeGeoKey);
        return rasterType == 2 ? RasterPixelAnchor.Point : RasterPixelAnchor.Area;
    }

    private static RasterGeoTransform? ReadGeoTransform(Tiff tiff, RasterPixelAnchor pixelAnchor)
    {
        var matrix = GetDoubleArray(tiff, ModelTransformationTag);
        RasterGeoTransform? transform = null;

        if (matrix is { Length: >= 16 })
        {
            transform = new RasterGeoTransform(
                matrix[3],
                matrix[0],
                matrix[1],
                matrix[7],
                matrix[4],
                matrix[5]);
        }
        else
        {
            var scale = GetDoubleArray(tiff, ModelPixelScaleTag);
            var tiePoint = GetDoubleArray(tiff, ModelTiepointTag);
            if (scale is { Length: >= 2 } && tiePoint is { Length: >= 6 })
            {
                var rasterX = tiePoint[0];
                var rasterY = tiePoint[1];
                var modelX = tiePoint[3];
                var modelY = tiePoint[4];
                transform = new RasterGeoTransform(
                    modelX - (rasterX * scale[0]),
                    scale[0],
                    0,
                    modelY + (rasterY * scale[1]),
                    0,
                    -scale[1]);
            }
        }

        if (transform is null || pixelAnchor != RasterPixelAnchor.Point)
        {
            return transform;
        }

        var corner = transform.Value.PixelToWorld(-0.5, -0.5);
        return transform.Value with { OriginX = corner.X, OriginY = corner.Y };
    }

    private static double? ReadNoData(Tiff tiff)
    {
        var values = tiff.GetField((TiffTag)GdalNoDataTag);
        if (values is null || values.Length == 0)
        {
            return null;
        }

        var text = values[^1].ToString().TrimEnd('\0').Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static RasterBandMetadata[] ReadBands(
        Tiff tiff,
        int samplesPerPixel,
        int bitsPerSample,
        double? noData)
    {
        var sampleFormat = GetOptionalInt(tiff, TiffTag.SAMPLEFORMAT) ?? 1;
        var photometric = GetDefaultedInt(tiff, TiffTag.PHOTOMETRIC);
        var sampleType = sampleFormat switch
        {
            1 => RasterSampleType.UnsignedInteger,
            2 => RasterSampleType.SignedInteger,
            3 => RasterSampleType.FloatingPoint,
            _ => RasterSampleType.Unknown,
        };
        var result = new RasterBandMetadata[samplesPerPixel];

        for (var band = 0; band < samplesPerPixel; band++)
        {
            result[band] = new RasterBandMetadata(
                band + 1,
                sampleType,
                bitsPerSample,
                GetColorInterpretation(photometric, band, samplesPerPixel),
                noData);
        }

        return result;
    }

    private static RasterColorInterpretation GetColorInterpretation(
        int photometric,
        int zeroBasedBand,
        int samplesPerPixel)
    {
        if (photometric is 0 or 1)
        {
            return zeroBasedBand == 0
                ? RasterColorInterpretation.Gray
                : RasterColorInterpretation.Alpha;
        }

        if (photometric is 2 or 6)
        {
            return zeroBasedBand switch
            {
                0 => RasterColorInterpretation.Red,
                1 => RasterColorInterpretation.Green,
                2 => RasterColorInterpretation.Blue,
                _ when zeroBasedBand == samplesPerPixel - 1 => RasterColorInterpretation.Alpha,
                _ => RasterColorInterpretation.Unknown,
            };
        }

        return photometric == 3 && zeroBasedBand == 0
            ? RasterColorInterpretation.Palette
            : RasterColorInterpretation.Unknown;
    }

    private static string GetColorModel(int photometric) => photometric switch
    {
        0 => "WhiteIsZero",
        1 => "BlackIsZero",
        2 => "RGB",
        3 => "Palette",
        5 => "Separated",
        6 => "YCbCr",
        _ => $"TIFF Photometric {photometric.ToString(CultureInfo.InvariantCulture)}",
    };

    private static RasterWindow MapWindowToDirectory(
        RasterWindow baseWindow,
        int baseWidth,
        int baseHeight,
        int directoryWidth,
        int directoryHeight)
    {
        var scaleX = (double)baseWidth / directoryWidth;
        var scaleY = (double)baseHeight / directoryHeight;
        var left = Math.Clamp((int)Math.Floor(baseWindow.X / scaleX), 0, directoryWidth - 1);
        var top = Math.Clamp((int)Math.Floor(baseWindow.Y / scaleY), 0, directoryHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling(baseWindow.Right / scaleX), left + 1, directoryWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(baseWindow.Bottom / scaleY), top + 1, directoryHeight);
        return new RasterWindow(left, top, right - left, bottom - top);
    }

    private static byte[] DecodeWindowRgba(
        Tiff tiff,
        TiffDirectoryDescriptor directory,
        RasterWindow window,
        CancellationToken cancellationToken)
    {
        if (!tiff.RGBAImageOK(out var errorMessage))
        {
            throw new NotSupportedException($"TIFF directory cannot be converted to RGBA: {errorMessage}");
        }

        var output = new byte[checked(window.Width * window.Height * 4)];
        if (directory.IsTiled)
        {
            DecodeTiledWindow(tiff, directory, window, output, cancellationToken);
        }
        else
        {
            DecodeStrippedWindow(tiff, directory, window, output, cancellationToken);
        }

        return output;
    }

    private static void DecodeTiledWindow(
        Tiff tiff,
        TiffDirectoryDescriptor directory,
        RasterWindow window,
        byte[] output,
        CancellationToken cancellationToken)
    {
        var tileWidth = GetRequiredInt(tiff, TiffTag.TILEWIDTH);
        var tileHeight = GetRequiredInt(tiff, TiffTag.TILELENGTH);
        var tilePixels = new int[checked(tileWidth * tileHeight)];
        var firstTileX = (window.X / tileWidth) * tileWidth;
        var firstTileY = (window.Y / tileHeight) * tileHeight;

        for (var tileY = firstTileY; tileY < window.Bottom; tileY += tileHeight)
        {
            for (var tileX = firstTileX; tileX < window.Right; tileX += tileWidth)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Array.Clear(tilePixels);
                if (!tiff.ReadRGBATile(tileX, tileY, tilePixels))
                {
                    throw new InvalidDataException($"Failed to decode TIFF tile at ({tileX}, {tileY}).");
                }

                var copyLeft = Math.Max(window.X, tileX);
                var copyRight = Math.Min(window.Right, Math.Min(directory.Width, tileX + tileWidth));
                var copyTop = Math.Max(window.Y, tileY);
                var copyBottom = Math.Min(window.Bottom, Math.Min(directory.Height, tileY + tileHeight));

                for (var sourceY = copyTop; sourceY < copyBottom; sourceY++)
                {
                    var localY = sourceY - tileY;
                    var bufferY = tileHeight - 1 - localY;
                    CopyRgbaSpan(
                        tilePixels,
                        bufferY * tileWidth,
                        tileX,
                        sourceY,
                        copyLeft,
                        copyRight,
                        window,
                        output);
                }
            }
        }
    }

    private static void DecodeStrippedWindow(
        Tiff tiff,
        TiffDirectoryDescriptor directory,
        RasterWindow window,
        byte[] output,
        CancellationToken cancellationToken)
    {
        var rowsPerStrip = Math.Min(GetDefaultedInt(tiff, TiffTag.ROWSPERSTRIP), directory.Height);
        var stripPixels = new int[checked(directory.Width * rowsPerStrip)];
        var firstStripY = (window.Y / rowsPerStrip) * rowsPerStrip;

        for (var stripY = firstStripY; stripY < window.Bottom; stripY += rowsPerStrip)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Array.Clear(stripPixels);
            if (!tiff.ReadRGBAStrip(stripY, stripPixels))
            {
                throw new InvalidDataException($"Failed to decode TIFF strip beginning at row {stripY}.");
            }

            var actualRows = Math.Min(rowsPerStrip, directory.Height - stripY);
            var copyTop = Math.Max(window.Y, stripY);
            var copyBottom = Math.Min(window.Bottom, stripY + actualRows);

            for (var sourceY = copyTop; sourceY < copyBottom; sourceY++)
            {
                var localY = sourceY - stripY;
                var bufferY = actualRows - 1 - localY;
                CopyRgbaSpan(
                    stripPixels,
                    bufferY * directory.Width,
                    0,
                    sourceY,
                    window.X,
                    window.Right,
                    window,
                    output);
            }
        }
    }

    private static void CopyRgbaSpan(
        int[] source,
        int sourceRowOffset,
        int sourceOriginX,
        int sourceY,
        int copyLeft,
        int copyRight,
        RasterWindow window,
        byte[] output)
    {
        var destinationY = sourceY - window.Y;
        for (var sourceX = copyLeft; sourceX < copyRight; sourceX++)
        {
            var packed = source[sourceRowOffset + sourceX - sourceOriginX];
            var destinationX = sourceX - window.X;
            var destinationOffset = ((destinationY * window.Width) + destinationX) * 4;
            output[destinationOffset] = checked((byte)Tiff.GetR(packed));
            output[destinationOffset + 1] = checked((byte)Tiff.GetG(packed));
            output[destinationOffset + 2] = checked((byte)Tiff.GetB(packed));
            output[destinationOffset + 3] = checked((byte)Tiff.GetA(packed));
        }
    }

    private static byte[] ResizeRgbaNearest(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight,
        CancellationToken cancellationToken)
    {
        if (sourceWidth == outputWidth && sourceHeight == outputHeight)
        {
            return source;
        }

        var output = new byte[checked(outputWidth * outputHeight * 4)];
        for (var y = 0; y < outputHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceY = Math.Min(sourceHeight - 1, ((2 * y + 1) * sourceHeight) / (2 * outputHeight));
            for (var x = 0; x < outputWidth; x++)
            {
                var sourceX = Math.Min(sourceWidth - 1, ((2 * x + 1) * sourceWidth) / (2 * outputWidth));
                var sourceOffset = ((sourceY * sourceWidth) + sourceX) * 4;
                var destinationOffset = ((y * outputWidth) + x) * 4;
                Buffer.BlockCopy(source, sourceOffset, output, destinationOffset, 4);
            }
        }

        return output;
    }

    private static int GetRequiredInt(Tiff tiff, TiffTag tag)
    {
        var values = tiff.GetField(tag);
        if (values is null || values.Length == 0)
        {
            throw new InvalidDataException($"Required TIFF tag {tag} is missing.");
        }

        return values[0].ToInt();
    }

    private static int GetDefaultedInt(Tiff tiff, TiffTag tag)
    {
        var values = tiff.GetFieldDefaulted(tag);
        if (values is null || values.Length == 0)
        {
            throw new InvalidDataException($"TIFF tag {tag} has no value or default.");
        }

        return values[0].ToInt();
    }

    private static int? GetOptionalInt(Tiff tiff, TiffTag tag)
    {
        var values = tiff.GetField(tag);
        return values is null || values.Length == 0 ? null : values[0].ToInt();
    }

    private static double[]? GetDoubleArray(Tiff tiff, int tag)
    {
        var values = tiff.GetField((TiffTag)tag);
        return values is null || values.Length == 0 ? null : values[^1].ToDoubleArray();
    }

    private static short[]? GetShortArray(Tiff tiff, int tag)
    {
        var values = tiff.GetField((TiffTag)tag);
        return values is null || values.Length == 0 ? null : values[^1].ToShortArray();
    }

    private static int? GetGeoKeyShortValue(short[] keys, int keyId)
    {
        var keyCount = (ushort)keys[3];
        for (var keyIndex = 0; keyIndex < keyCount; keyIndex++)
        {
            var offset = 4 + (keyIndex * 4);
            if (offset + 3 >= keys.Length)
            {
                break;
            }

            var id = (ushort)keys[offset];
            var location = (ushort)keys[offset + 1];
            var count = (ushort)keys[offset + 2];
            if (id == keyId && location == 0 && count == 1)
            {
                return (ushort)keys[offset + 3];
            }
        }

        return null;
    }

    private static string? ReadGeoCitation(Tiff tiff, short[] keys, int keyId)
    {
        var asciiValues = tiff.GetField((TiffTag)GeoAsciiParamsTag);
        if (asciiValues is null || asciiValues.Length == 0)
        {
            return null;
        }

        var text = asciiValues[^1].ToString();
        var keyCount = (ushort)keys[3];
        for (var keyIndex = 0; keyIndex < keyCount; keyIndex++)
        {
            var offset = 4 + (keyIndex * 4);
            if (offset + 3 >= keys.Length)
            {
                break;
            }

            var id = (ushort)keys[offset];
            var location = (ushort)keys[offset + 1];
            var count = (ushort)keys[offset + 2];
            var valueOffset = (ushort)keys[offset + 3];
            if (id != keyId || location != GeoAsciiTagLocation || count == 0 || valueOffset >= text.Length)
            {
                continue;
            }

            var length = Math.Min(count, text.Length - valueOffset);
            return text.Substring(valueOffset, length).TrimEnd('|', '\0').Trim();
        }

        return null;
    }

    private static void SetDirectory(Tiff tiff, short directoryIndex)
    {
        if (!tiff.SetDirectory(directoryIndex))
        {
            throw new InvalidDataException($"Unable to select TIFF directory {directoryIndex}.");
        }
    }

    private static void EnsureSupportedOrientation(Tiff tiff)
    {
        var orientation = GetDefaultedInt(tiff, TiffTag.ORIENTATION);
        if (orientation != 1)
        {
            throw new NotSupportedException(
                $"TIFF orientation {orientation} is not supported by the Phase 3 managed GeoTIFF reader. Expected top-left orientation (1).");
        }
    }

    private sealed record TiffDirectoryDescriptor(
        short DirectoryIndex,
        int Width,
        int Height,
        int SamplesPerPixel,
        int BitsPerSample,
        int Photometric,
        bool IsTiled);
}
