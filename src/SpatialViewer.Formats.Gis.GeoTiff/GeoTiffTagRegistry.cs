using System.Runtime.CompilerServices;
using BitMiracle.LibTiff.Classic;

namespace SpatialViewer.Formats.Gis.GeoTiff;

internal static class GeoTiffTagRegistry
{
    private static readonly TiffFieldInfo[] FieldInfo =
    {
        CreateDoubleField(33550, "ModelPixelScaleTag"),
        CreateDoubleField(33922, "ModelTiepointTag"),
        CreateDoubleField(34264, "ModelTransformationTag"),
        CreateShortField(34735, "GeoKeyDirectoryTag"),
        CreateDoubleField(34736, "GeoDoubleParamsTag"),
        CreateAsciiField(34737, "GeoAsciiParamsTag"),
        CreateAsciiField(42112, "GDAL_METADATA"),
        CreateAsciiField(42113, "GDAL_NODATA"),
    };

    private static Tiff.TiffExtendProc? _parentExtender;

    [ModuleInitializer]
    internal static void Initialize()
    {
        _parentExtender = Tiff.SetTagExtender(RegisterTags);
    }

    private static void RegisterTags(Tiff tiff)
    {
        tiff.MergeFieldInfo(FieldInfo, FieldInfo.Length);
        _parentExtender?.Invoke(tiff);
    }

    private static TiffFieldInfo CreateDoubleField(int tag, string name) =>
        new(
            (TiffTag)tag,
            TiffFieldInfo.Variable2,
            TiffFieldInfo.Variable2,
            TiffType.DOUBLE,
            FieldBit.Custom,
            true,
            true,
            name);

    private static TiffFieldInfo CreateShortField(int tag, string name) =>
        new(
            (TiffTag)tag,
            TiffFieldInfo.Variable2,
            TiffFieldInfo.Variable2,
            TiffType.SHORT,
            FieldBit.Custom,
            true,
            true,
            name);

    private static TiffFieldInfo CreateAsciiField(int tag, string name) =>
        new(
            (TiffTag)tag,
            TiffFieldInfo.Variable2,
            TiffFieldInfo.Variable2,
            TiffType.ASCII,
            FieldBit.Custom,
            true,
            true,
            name);
}
