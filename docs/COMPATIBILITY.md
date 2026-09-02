# GIS Compatibility Matrix

| Capability | Stage | Current status / notes |
| --- | --- | --- |
| GeoJSON | P0 / 0.1 | **Phase 1 implemented**: FeatureCollection, Feature, direct Geometry; Point/MultiPoint/LineString/MultiLineString/Polygon/MultiPolygon/GeometryCollection; ids, nested properties, null geometry, bbox, Z, extent filtering and read-to-render conversion |
| Shapefile | P0 / 0.2 | **Phase 2 implemented (managed)**: SHP/SHX/DBF/PRJ/CPG; Point/MultiPoint/PolyLine/Polygon 2D/Z/M; DBF attributes/encoding; missing PRJ remains Unknown; extent queries use lightweight record bounds plus packed R-tree candidates |
| GeoPackage | P0 / 0.2 | **Phase 2 vector implemented**: feature tables, geometry columns, attributes, nullable geometry, GeoPackageBinary/WKB, Z/M, SRS_ID validation, layer metadata, and file RTree-assisted extent queries. Raster/tile tables remain outside the current adapter scope |
| CRS / EPSG / WKT | P0 / 0.2 | **Phase 2 managed baseline implemented**: WKT1/WKT2 EPSG authority extraction, unknown WKT preservation, explicit axis-order policy, EPSG:4326 ↔ EPSG:3857 transform with Z/M preservation. Broader CRS coverage remains a future PROJ adapter concern |
| Spatial index / streaming | P0 / 0.2 | **Phase 2 implemented baseline**: immutable backend-neutral packed R-tree; Shapefile indexes record envelopes/references rather than full features; GeoPackage reuses native RTree tables when available; feature APIs remain async streaming |
| GeoTIFF / local COG-compatible TIFF | P0 / 0.3 | **Phase 3 implemented managed baseline**: GeoKey EPSG, ModelPixelScale/Tiepoint and ModelTransformation, PixelIsArea/PixelIsPoint, nodata, band/color metadata, internal overviews, tiled and stripped window reads. Local viewport reads decode only intersecting tiles/strips |
| Remote COG | P1 / 0.4 | **Phase 4 implemented baseline**: HTTP Range-backed tiled GeoTIFF reads via LibTiff.NET, internal overviews, bounded byte-range cache, stable Content-Range length validation. Server must return `206 Partial Content`; a `200` full-file response is rejected. Remote stripped TIFF and full COG conformance validation are not claimed |
| PNG/JPEG + world file | P1 / 0.3 | **Phase 3 implemented**: PNG/JPEG metadata/pixel decode, PGW/JGW/long-form/WLD sidecars, rotated affine transforms, optional same-name PRJ, viewport resampling. PNG/JPEG are not random-tile containers: first pixel read decodes the complete compressed image, then weak-reference and viewport caches can reuse it |
| Raster viewport/cache | P0 / 0.3 | **Phase 3 implemented**: raster pixel-window contract, overview selector, byte-budgeted LRU cache, latest-request coordinator, `RasterViewportReader` integration, cancellation of superseded viewport reads |
| Tile core / cache | P1 / 0.4 | **Phase 4 implemented**: canonical north-origin XYZ coordinates, explicit TMS conversion at adapter boundaries, Web Mercator tile bounds/resolution, encoded tile payload contract, byte-budgeted tile LRU and latest-request cancellation |
| MBTiles | P1 / 0.4 | **Phase 4 implemented**: metadata/tiles SQLite tables, TMS `tile_row` storage converted to canonical XYZ, PNG/JPEG/WebP/MVT encoded payloads, bounds/zoom/attribution metadata. SQLite connections are read-only, private-cache and non-pooled |
| XYZ / TMS HTTP | P1 / 0.4 | **Phase 4 implemented**: `{z}/{x}/{y}` template path, canonical XYZ ↔ TMS Y conversion, timeout, caller cancellation, 404→null, transient retry, ETag/Last-Modified and byte-budget cache. Authentication/provider-specific templating is an upper-layer concern |
| WMS | P1 / 0.4 | **Phase 4 implemented baseline**: WMS 1.3.0 GetMap with explicit BBOX/size/format/CRS parameters and separate backend-neutral map-image contract. EPSG:4326 uses WMS 1.3.0 latitude-first BBOX order. Capabilities parsing, GetFeatureInfo and vendor extensions are not yet implemented |
| WMTS | P1 / 0.4 | **Phase 4 implemented baseline**: WMTS 1.0.0 KVP GetTile, TileMatrix identifier template, XYZ/TMS row conversion and tile caching. Full GetCapabilities/TileMatrixSet discovery and RESTful resource templates remain future compatibility work |
| MVT | P1 / 0.4 | **Phase 4 implemented managed baseline**: in-house protobuf reader, typed values/tags, delta+zigzag geometry commands, Point/MultiPoint/LineString/MultiLineString/Polygon/MultiPolygon, conversion from tile-local coordinates to EPSG:3857 `GisFeature` geometry. Styling is Phase 5 |
| PMTiles | P1 / 0.4 | **Phase 4 implemented v3 baseline**: local random reads and remote HTTP Range, 127-byte v3 header, Hilbert TileID, root/leaf varint directories, None/GZip/Brotli decompression, MVT/PNG/JPEG/WebP tile types. Remote `200` full-file fallback is rejected. Zstd compression, AVIF, MapLibre Vector Tile and zoom >30 are explicit unsupported boundaries in the current managed contract |
| KML / KMZ / GPX | P1 | Planned interchange/track formats; not part of the completed Phase 4 tile/network milestone |
| FileGDB | P2 | Prefer future GDAL/OGR adapter; redistribution/license review required |
| PostGIS | P2 | Optional database adapter; not part of local-file MVP |

## Support boundary

“Implemented” in this matrix means the repository contains a concrete adapter/service and CI-backed regression coverage for the stated scope. It does not imply every vendor extension, TIFF photometric/layout combination, web-service capability document, MVT styling convention, PMTiles compression mode, geometry variant, or CRS is accepted.

Core/public contracts remain independent from SQLite, LibTiff.NET, StbImageSharp, GDAL/OGR, PROJ and HTTP implementation details. Concrete dependencies stay inside adapters and may be replaced without exposing their handles or types to SpatialViewer UI code.

Phase 3's large-raster guarantee remains specific to the GeoTIFF tile/strip path: a requested viewport does not require construction of a whole-image RGBA buffer. PNG/JPEG remain documented whole-image-first-decode exceptions. Phase 4 extends the same non-hidden-download rule to remote COG and PMTiles: remote random-access paths require HTTP Range semantics and reject servers that silently return a complete file with HTTP 200.

Core tile coordinates are canonical XYZ. MBTiles/TMS/WMTS adapters convert storage/service row semantics at their boundaries so the UI and MVT geometry path do not inherit competing Y-axis conventions.

Million-scale vector benchmarks, genuinely large raster wall-clock/peak-memory/cache-pressure measurements, and production-scale network cache/latency benchmarks remain reserved for the Phase 6 performance program rather than being inferred from small deterministic fixtures.
