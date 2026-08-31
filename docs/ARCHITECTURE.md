# GisCore Architecture / 内核架构 / アーキテクチャ

## Boundary

`SpatialViewer.GisCore` owns the GIS ingestion-to-render pipeline. `SpatialViewer` owns product UI and interaction.

```text
GIS source (file / database / tile / service)
  -> Format Adapter
  -> source metadata + CRS
  -> vector or raster semantic layer
  -> explicit projection service (only when requested)
  -> extent query / lazy loading / spatial index
  -> GIS-to-Render translator
  -> backend-neutral GisRenderFrame
  -> optional Windows renderer
  -> SpatialViewer UI surface
```

## Project layers

- `SpatialViewer.Gis.Core`: domain primitives for coordinates, extents, features/layers, spatial references, and the backend-neutral packed R-tree.
- `SpatialViewer.Formats.Gis`: reader/probe contracts; no concrete third-party backend.
- `SpatialViewer.Formats.Gis.GeoJson`: managed GeoJSON reference adapter.
- `SpatialViewer.Formats.Gis.Shapefile`: managed SHP/SHX/DBF/PRJ/CPG adapter; preserves Z/M and builds record-envelope candidates without materializing every feature.
- `SpatialViewer.Formats.Gis.GeoPackage`: GeoPackage vector adapter; parses GeoPackageBinary/WKB and uses SQLite/GeoPackage RTree tables when present.
- `SpatialViewer.Gis.Projections`: explicit CRS parsing/coordinate-transform boundary. The managed baseline recognizes EPSG/WKT metadata and implements tested EPSG:4326 ↔ EPSG:3857 transforms with explicit axis-order policy.
- `SpatialViewer.Gis.Rendering`: backend-neutral render-frame contracts.
- `SpatialViewer.Gis.Rendering.Windows`: Windows-specific rendering integration boundary.

## Dependency direction

- Core knows nothing about readers, SQLite, GDAL, PROJ, rendering backends, or UI.
- Format adapters depend on Core, the reader contracts, and only their own implementation dependencies.
- Projection services depend on Core and expose only GisCore types.
- Rendering abstraction depends on Core only.
- Windows rendering depends on the rendering abstraction.
- SpatialViewer depends on GisCore; GisCore must never reference SpatialViewer.App/Presentation.
- Future GDAL/OGR or PROJ integrations must live behind adapter/service boundaries and must not replace the managed public contracts.

## Data rules

1. Never reinterpret an unknown CRS as WGS84.
2. Preserve source X/Y/Z/M coordinates and CRS metadata until an explicit transform is requested.
3. Axis order is a caller-visible transform policy, never an implicit format-side correction.
4. Keep vector features and raster pixels/tiles on separate memory paths.
5. Prefer streaming and extent-based reads over loading complete datasets.
6. Spatial indexes store lightweight envelopes/record references, not complete feature payloads.
7. Do not expose SQLite/GDAL/OGR/PROJ handles or third-party geometry objects across public boundaries.
