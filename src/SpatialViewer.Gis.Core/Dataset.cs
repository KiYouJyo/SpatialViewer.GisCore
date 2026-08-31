namespace SpatialViewer.Gis.Core;

public sealed record GisDatasetMetadata(
    string DisplayName,
    string SourceKind,
    IReadOnlyList<GisLayerMetadata> Layers);

public sealed class GisReadException : Exception
{
    public GisReadException(string message)
        : base(message)
    {
    }

    public GisReadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
