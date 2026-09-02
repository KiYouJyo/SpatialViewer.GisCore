# Changelog

All notable changes to SpatialViewer.GisCore will be documented in this file.

## [Unreleased]

Future work begins with Phase 5 GIS display semantics and later Phase 6 performance/compatibility convergence.

## [0.4.0] - Completed 2026-09-02

### Added
- Backend-neutral tile-domain contracts: canonical XYZ coordinates, explicit TMS conversion, Web Mercator tile math, encoded payload metadata, byte-budgeted tile cache, latest-request cancellation, and `TileViewportReader`.
- Managed MBTiles adapter for metadata/tiles tables with TMS-row normalization and PNG/JPEG/WebP/MVT payloads.
- XYZ/TMS HTTP template adapter with timeout, caller cancellation, 404 handling, transient retry, ETag/Last-Modified retention, and bounded caching.
- HTTP Range-backed remote COG adapter for tiled GeoTIFF and internal overviews. Servers must return `206 Partial Content`; HTTP 200 full-file fallback is rejected.
- WMS 1.3.0 GetMap baseline with a dedicated map-image contract and explicit EPSG:4326 latitude-first BBOX semantics.
- WMTS 1.0.0 KVP GetTile baseline with TileMatrix templates, row-scheme conversion, and cache integration.
- Managed MVT protobuf decoder for values/tags and Point/MultiPoint/LineString/MultiLineString/Polygon/MultiPolygon conversion into `GisFeature` geometry in EPSG:3857.
- Managed PMTiles v3 reader for local random access and HTTP Range, v3 header parsing, Hilbert TileID lookup, root/leaf varint directories, None/GZip/Brotli decompression, and MVT/PNG/JPEG/WebP payloads.
- Deterministic protocol/archive regression tests for Range behavior, retries/timeouts/cancellation, TMS/XYZ conversion, WMS/WMTS request semantics, MVT geometry, and PMTiles local/remote reads.

### Changed
- The repository version baseline advances to `0.4.0`.
- Tile coordinates are canonical XYZ throughout Core/UI-facing contracts; MBTiles/TMS/WMTS adapters convert row semantics only at their boundaries.
- Network and random-access adapters use independent bounded caches instead of mixing encoded network payloads with raster RGBA cache entries.
- All Phase 4 projects are direct Debug/Release solution members so CI validates their actual Release configuration.
- `Microsoft.Data.Sqlite` is now used by both GeoPackage and MBTiles adapters; LibTiff.NET is used by both local GeoTIFF and remote COG adapters.

### Explicit boundaries
- Remote COG baseline is tiled GeoTIFF + HTTP Range + internal overviews; remote stripped TIFF and exhaustive COG conformance validation are not claimed.
- WMS baseline does not yet include GetCapabilities/GetFeatureInfo; WMTS baseline does not yet parse full Capabilities/TileMatrixSet discovery.
- PMTiles v3 Zstd compression, AVIF, MapLibre Vector Tile, and zoom levels above the current Core limit of 30 are explicitly unsupported or untyped.
- MVT styling remains Phase 5; Phase 4 decodes data/geometry only.

## [0.3.0] - Merged 2026-08-31

Phase 3 raster milestone: GeoTIFF local window/overview ingestion, PNG/JPEG world-image georeferencing, raster cache/viewport cancellation, and CI-backed raster fixtures. The later Phase 4 milestone added remote HTTP Range COG access.

### Added
- Raster domain contracts for affine geotransforms, pixel windows, pixel anchors, bands, nodata, overviews, RGBA results, and raster reader adapters.
- Managed GeoTIFF adapter with GeoKey EPSG metadata, ModelPixelScale/Tiepoint and ModelTransformation, PixelIsArea/PixelIsPoint handling, nodata/color metadata, internal overviews, and tile/strip window decoding.
- PNG/JPEG world-image adapter with PGW/JGW/long-form/WLD sidecars, optional PRJ, rotated affine georeferencing, and managed RGBA decoding.
- Byte-budgeted raster LRU cache, overview selector, latest-request coordinator, and `RasterViewportReader` integration that cancels superseded viewport work.
- Synthetic Deflate tiled/overview GeoTIFF, stripped GeoTIFF, PNG/world-file, and JPEG/world-file regression fixtures.

## [0.2.0] - Merged 2026-08-31

Phase 2 implementation milestone: GeoJSON + Shapefile + GeoPackage vector ingestion, managed CRS baseline, explicit coordinate transforms, and spatial-index foundations.

### Added
- Managed Shapefile reader for SHP/SHX/DBF/PRJ/CPG with Point/MultiPoint/PolyLine/Polygon 2D/Z/M, DBF encoding, projection metadata, and record-index validation.
- Managed GeoPackage vector reader for feature tables, geometry columns, attributes, nullable geometry, GeoPackageBinary/WKB, Z/M, SRS_ID validation, and RTree-assisted extent queries.
- Explicit projection service with WKT1/WKT2 EPSG authority parsing, axis-order policy, and tested EPSG:4326 ↔ EPSG:3857 transforms preserving Z/M.
- Backend-neutral immutable packed R-tree plus lightweight Shapefile record-envelope candidate filtering.

## [0.1.0] - Foundation

Foundation milestone: stable core contracts plus first end-to-end GeoJSON read-to-render path.

### Added
- Initial repository scaffold aligned with SpatialViewer.CadCore conventions.
- GIS domain primitives for coordinates, extents, spatial references, features, and layers.
- Complete managed Phase 1 GeoJSON reader for FeatureCollection, Feature and direct Geometry roots.
- Point, MultiPoint, LineString, MultiLineString, Polygon, MultiPolygon and GeometryCollection semantic models with nullable/empty geometry handling.
- Feature ids, nested JSON attributes, null values, declared 2D/3D bbox, Z coordinates and explicit legacy named CRS preservation.
- Extent filtering and backend-neutral vector RenderFrame conversion with typed Point/Polyline/Polygon payloads.
- Synthetic malformed and mixed-geometry regression fixtures plus generated 4096-feature stress coverage.

## Cross-version rules

- Missing CRS remains explicitly `Unknown`; readers never invent EPSG:4326.
- Polygon rings and coordinate dimensions are validated without silently closing rings or discarding extra ordinates.
- `GisCoordinate` preserves optional M in addition to X/Y/Z.
- Third-party implementation types remain isolated behind SpatialViewer-owned public contracts.
