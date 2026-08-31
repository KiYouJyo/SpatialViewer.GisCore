# GisCore Architecture / 内核架构 / アーキテクチャ

## Boundary

`SpatialViewer.GisCore` owns the GIS ingestion-to-render pipeline. `SpatialViewer` owns product UI and interaction.

```text
GIS source (file / database / tile / service)
  -> Format Adapter
  -> source metadata + CRS
  -> vector or raster semantic layer
  -> extent query / lazy loading / spatial index
  -> GIS-to-Render translator
  -> backend-neutral GisRenderFrame
  -> optional Windows renderer
  -> SpatialViewer UI surface
```

## Project layers

- `SpatialViewer.Gis.Core`: CRS-neutral domain primitives, extents, feature/layer contracts.
- `SpatialViewer.Formats.Gis`: reader/probe contracts; no concrete third-party backend.
- `SpatialViewer.Formats.Gis.GeoJson`: first managed format adapter and reference implementation.
- `SpatialViewer.Gis.Rendering`: backend-neutral render-frame contracts.
- `SpatialViewer.Gis.Rendering.Windows`: Windows-specific rendering integration boundary.

## Dependency direction

- Core knows nothing about readers, GDAL, PROJ, rendering backends, or UI.
- Format adapters depend on Core and their own third-party implementation only.
- Rendering abstraction depends on Core only.
- Windows rendering depends on the rendering abstraction.
- SpatialViewer depends on GisCore; GisCore must never reference SpatialViewer.App/Presentation.

## Data rules

1. Never reinterpret an unknown CRS as WGS84.
2. Preserve source coordinates and CRS metadata until an explicit transform is requested.
3. Keep vector features and raster pixels/tiles on separate memory paths.
4. Prefer streaming and extent-based reads over loading complete datasets.
5. Do not expose GDAL/OGR/PROJ handles or third-party geometry objects across public boundaries.
