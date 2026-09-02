# GisCore Architecture / 内核架构 / アーキテクチャ

## Boundary

`SpatialViewer.GisCore` owns the GIS ingestion-to-render pipeline. `SpatialViewer` owns product UI and interaction.

```text
GIS source (file / database / tile / service)
  -> Format / Service Adapter
  -> source metadata + CRS / tile scheme
  -> vector / raster / encoded tile / map-image semantic path
  -> explicit projection service (only when requested)
  -> extent/window/tile/range query + cache + cancellation
  -> optional decode/translation stage
  -> backend-neutral render payload
  -> optional Windows renderer
  -> SpatialViewer UI surface
```

## Project layers

- `SpatialViewer.Gis.Core`: domain primitives for coordinates, extents, features/layers, spatial references, raster windows/affine transforms, raster metadata/results, canonical tile coordinates, Web Mercator tile math, cache/coordinator primitives, and the backend-neutral packed R-tree.
- `SpatialViewer.Formats.Gis`: vector/raster/tile/map-image reader contracts plus viewport coordinators. It owns no concrete codec, HTTP provider or database implementation.
- `SpatialViewer.Formats.Gis.GeoJson`: managed GeoJSON reference adapter.
- `SpatialViewer.Formats.Gis.Shapefile`: managed SHP/SHX/DBF/PRJ/CPG adapter; preserves Z/M and builds record-envelope candidates without materializing every feature.
- `SpatialViewer.Formats.Gis.GeoPackage`: GeoPackage vector adapter; parses GeoPackageBinary/WKB and uses SQLite/GeoPackage RTree tables when present.
- `SpatialViewer.Formats.Gis.GeoTiff`: managed local GeoTIFF adapter using LibTiff.NET internally. It reads georeferencing/GeoKeys/nodata/band metadata and decodes only requested intersecting tiles or strips, selecting internal overview directories when appropriate.
- `SpatialViewer.Formats.Gis.WorldImage`: PNG/JPEG + world-file adapter using StbImageSharp internally.
- `SpatialViewer.Formats.Gis.MbTiles`: read-only SQLite MBTiles adapter. Physical TMS rows are normalized to canonical XYZ at the adapter boundary; encoded raster/MVT payloads are returned without leaking SQLite types.
- `SpatialViewer.Formats.Gis.XyzTiles`: HTTP XYZ/TMS template adapter with timeout, retry, cancellation, validators and byte-budgeted tile caching.
- `SpatialViewer.Formats.Gis.RemoteCog`: HTTP Range-backed tiled GeoTIFF/overview adapter. It bridges LibTiff.NET's synchronous random-read stream contract to bounded range requests and rejects non-Range servers instead of downloading complete files.
- `SpatialViewer.Formats.Gis.WebMap`: WMS 1.3.0 map-image and WMTS 1.0 KVP tile adapters. WMS intentionally uses a separate map-image contract because arbitrary BBOX images are not tile coordinates.
- `SpatialViewer.Formats.Gis.Mvt`: managed protobuf/MVT decoder that converts tile-local geometry and attributes to existing `GisFeature` / `IGisGeometry` in EPSG:3857.
- `SpatialViewer.Formats.Gis.PmTiles`: managed PMTiles v3 archive adapter with local random reads and HTTP Range, Hilbert TileID lookup, varint directories and supported decompression boundaries.
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

For GeoTIFF, the source pixel window stays expressed in base-resolution pixel coordinates even if the adapter chooses an overview. `RasterGeoTransform` uses pixel *corners* as its geometric frame; GeoTIFF `PixelIsPoint` and world-file pixel-center semantics are normalized at adapter boundaries.

## Tile and network pipeline

```text
local/remote tile source
  -> ITileDataSourceReader
  -> TileSourceMetadata
  -> canonical XYZ TileCoordinate
       adapter converts TMS/service row conventions only at boundary
  -> TileViewportReader / byte-budgeted tile cache / latest-request cancellation
  -> encoded TileReadResult (PNG/JPEG/WebP/MVT)
       raster tile -> presentation decoder
       MVT -> managed MvtTileDecoder -> GisFeature / IGisGeometry
  -> render path
```

Core uses one north-origin XYZ coordinate convention. MBTiles, TMS URL templates and WMTS configurations may store/expose different row conventions, but those differences never escape their adapters.

WMS is deliberately separate:

```text
WMS endpoint + MapImageRequest(BBOX, CRS, width, height)
  -> WMS 1.3.0 GetMap adapter
  -> encoded MapImageResult
```

This prevents an arbitrary WMS BBOX response from being misrepresented as a slippy-map tile. For EPSG:4326, the WMS 1.3.0 adapter applies latitude-first BBOX order explicitly.

## Remote random-access rule

Remote COG and remote PMTiles are random-access paths, not download helpers.

- Each request must be represented by HTTP `Range`.
- The server must respond with `206 Partial Content` and a valid `Content-Range`.
- A server that ignores Range and returns HTTP 200 is rejected.
- No adapter silently downloads the whole object as a compatibility fallback.
- Caches are bounded independently from raster/tile semantic caches.

Remote COG uses bounded fixed-size range blocks behind LibTiff.NET's synchronous `TiffStream` random-read interface. PMTiles reads only header/directory/tile ranges required by a requested Z/X/Y.

## MVT and PMTiles boundaries

MVT protobuf decoding is SpatialViewer-owned code; no protobuf runtime type enters public contracts. Tile-local Y-down coordinates are transformed through the canonical Web Mercator tile bounds into EPSG:3857 geometry.

PMTiles v3 remains an archive adapter, not a new public tile model. Its 64-bit archive offsets are kept unsigned while parsing and converted to .NET `Int64` only at the file/HTTP boundary with explicit range checks. The managed 0.4.0 baseline supports None/GZip/Brotli and MVT/PNG/JPEG/WebP. Unsupported Zstd/AVIF/MapLibre Vector Tile modes fail or remain Unknown explicitly rather than being misdecoded.

## Dependency direction

- Core knows nothing about readers, HTTP, SQLite, LibTiff.NET, StbImageSharp, GDAL, PROJ, rendering backends, or UI.
- Format/service adapters depend on Core, reader contracts, and only their own implementation dependencies.
- `SpatialViewer.Formats.Gis` coordinates Core cache/cancellation primitives but contains no concrete codec/backend dependency.
- Projection services depend on Core and expose only GisCore types.
- Rendering abstraction depends on Core only.
- Windows rendering depends on the rendering abstraction.
- SpatialViewer depends on GisCore; GisCore must never reference SpatialViewer.App/Presentation.
- Future GDAL/OGR or PROJ integrations must live behind adapter/service boundaries and must not replace the managed public contracts.

## Data rules

1. Never reinterpret an unknown CRS as WGS84.
2. Preserve source X/Y/Z/M coordinates and CRS metadata until an explicit transform is requested.
3. Axis order is a caller-visible transform/service policy, never an invisible data rewrite.
4. Keep vector features, raster pixels, encoded tiles and map images on separate memory paths.
5. Prefer streaming and extent/window/tile/range reads over loading complete datasets.
6. Spatial indexes store lightweight envelopes/record references, not complete feature payloads.
7. Tiled/stripped GeoTIFF viewport reads must not allocate a full-image RGBA buffer.
8. PNG/JPEG whole-image first decode remains a documented codec limitation.
9. New viewport/tile requests cancel superseded work where practical; adapters observe cancellation at network/block boundaries.
10. Canonical Core tile coordinates are XYZ; TMS/storage/service row transforms happen only in adapters.
11. Remote COG/PMTiles must never hide a complete-object download behind a random-access API.
12. Do not expose HttpClient/SQLite/LibTiff.NET/StbImageSharp/GDAL/OGR/PROJ handles or third-party geometry/image objects across public boundaries.
