namespace SpatialViewer.Gis.Core;

public sealed record GisDatasetMetadata(
    string DisplayName,
    string SourceKind,
    IReadOnlyList<GisLayerMetadata> Layers);
