# GisCore Architecture / 内核架构 / アーキテクチャ

## Boundary

`SpatialViewer.GisCore` owns the GIS ingestion-to-render pipeline. `SpatialViewer` owns product UI and interaction.

```text
GIS source (file / database / tile / service)
  -> Format Adapter
  -> source metadata + CRS
  -> vector or raster semantic layer
  -> explicit projection service (only when requested)
  -> extent/window query / lazy loading / spatial index / raster cache
  -> GIS-to-Render translator
  -> backend-neutral render payload
  -> optional Windows renderer
  -> SpatialViewer UI surface
```

## Project layers

- `SpatialViewer.Gis.Core`: domain primitives for coordinates, extents, features/layers, spatial references, raster windows/affine transforms, raster metadata/results, cache/coordinator primitives, and the backend-neutral packed R-tree.
- `SpatialViewer.Formats.Gis`: vector/raster reader contracts plus `RasterViewportReader`, which integrates byte-budgeted caching with latest-request cancellation without depending on a concrete image library.
- `SpatialViewer.Formats.Gis.GeoJson`: managed GeoJSON reference adapter.
- `SpatialViewer.Formats.Gis.Shapefile`: managed SHP/SHX/DBF/PRJ/CPG adapter; preserves Z/M and builds record-envelope candidates without materializing every feature.
- `SpatialViewer.Formats.Gis.GeoPackage`: GeoPackage vector adapter; parses GeoPackageBinary/WKB and uses SQLite/GeoPackage RTree tables when present.
- `SpatialViewer.Formats.Gis.GeoTiff`: managed GeoTIFF adapter using LibTiff.NET internally. It reads georeferencing/GeoKeys/nodata/band metadata and decodes only requested intersecting tiles or strips, selecting internal overview directories when appropriate.
- `SpatialViewer.Formats.Gis.WorldImage`: PNG/JPEG + world-file adapter using StbImageSharp internally. It normalizes world-file pixel-center coordinates to the Core corner-based affine transform and accepts optional same-name PRJ.
- `SpatialViewer.Gis.Projections`: explicit CRS parsing/coordinate-transform boundary. The managed baseline recognizes EPSG/WKT metadata and implements tested EPSG:4326 ↔ EPSG:3857 transforms with explicit axis-order policy.
- `SpatialViewer.Gis.Rendering`: backend-neutral vector render-frame contracts.
- `SpatialViewer.Gis.Rendering.Windows`: Windows-specific rendering integration boundary.

## Raster pipeline

```text
Raster source
  -> IRasterDataSourceReader
  -> RasterLayerMetadata
       dimensions / CRS / affine GeoTransform
       bands / nodata / color interpretation
       overview list / PixelIsArea|PixelIsPoint
  -> RasterViewportReader
       cancel superseded viewport request
       lookup byte-budgeted LRU cache
       request RasterWindow + output size
  -> adapter selects overview / source blocks
  -> decode only necessary GeoTIFF tile/strip blocks
  -> RGBA RasterReadResult
  -> cache / presentation layer
```

For GeoTIFF, the source pixel window stays expressed in base-resolution pixel coordinates even if the adapter chooses an overview. This keeps UI viewport math stable while allowing the adapter to map the requested window into a lower-resolution directory internally.

`RasterGeoTransform` uses pixel *corners* as its geometric frame. GeoTIFF `PixelIsPoint` and world-file pixel-center semantics are normalized at the adapter boundary while the original `RasterPixelAnchor` metadata remains visible where applicable.

## Dependency direction

- Core knows nothing about readers, SQLite, LibTiff.NET, StbImageSharp, GDAL, PROJ, rendering backends, or UI.
- Format adapters depend on Core, the reader contracts, and only their own implementation dependencies.
- `SpatialViewer.Formats.Gis` may coordinate Core cache/cancellation primitives but contains no concrete codec/backend dependency.
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
5. Prefer streaming and extent/window-based reads over loading complete datasets.
6. Spatial indexes store lightweight envelopes/record references, not complete feature payloads.
7. For tiled/stripped GeoTIFF, do not allocate a full-image RGBA buffer for a viewport request.
8. PNG/JPEG whole-image first decode is a documented codec limitation, not a claim of tiled random access.
9. New viewport requests cancel superseded raster work; adapters must observe cancellation at practical block/row boundaries.
10. Do not expose SQLite/LibTiff.NET/StbImageSharp/GDAL/OGR/PROJ handles or third-party geometry/image objects across public boundaries.
