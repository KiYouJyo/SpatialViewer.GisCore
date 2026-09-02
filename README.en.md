# SpatialViewer.GisCore

[中文](README.md) · [日本語](README.ja.md) · [English](README.en.md)

Independent GIS viewing core for SpatialViewer. This repository owns GIS data-source adapters, spatial-reference semantics, vector/raster/tile domain models, spatial querying, caching/cancellation, rendering abstractions, and regression tests. The WinUI 3 product UI remains in `KiYouJyo/SpatialViewer`.

> Current stage: **Phase 4 / 0.4.0 tile and network-source baseline completed**. In addition to GeoJSON, Shapefile, GeoPackage, GeoTIFF, world images and CRS foundations, the managed baseline now includes MBTiles, XYZ/TMS, WMS/WMTS, HTTP Range remote COG, managed MVT, PMTiles v3, and shared tile cache/cancellation contracts.

## Current capabilities

- **GeoJSON**: common Geometry families, Feature/FeatureCollection, properties, bbox, Z, safe missing-CRS behavior, and RenderFrame conversion.
- **Shapefile**: SHP/SHX/DBF/PRJ/CPG; 2D/Z/M Point/MultiPoint/PolyLine/Polygon; DBF encodings, PRJ CRS, and extent candidate indexing.
- **GeoPackage**: feature tables, geometry columns, attributes, nullable geometry, GeoPackageBinary/WKB, Z/M, SRS_ID validation, and file RTree-assisted extent queries.
- **CRS / Transform**: WKT1/WKT2 EPSG authority recognition, preservation of unknown WKT, explicit axis-order policy, and EPSG:4326 ↔ EPSG:3857 transforms.
- **GeoTIFF / local COG**: GeoKeys, affine georeferencing, PixelIsArea/PixelIsPoint, nodata, band/color metadata, internal overviews, and tile/strip window reads.
- **Remote COG**: HTTP Range random access for tiled GeoTIFF and internal overviews. A server must return `206 Partial Content`; ignored Range requests returning HTTP 200 are rejected rather than silently downloading the whole file.
- **PNG/JPEG + world file**: PGW/JGW/long-form/WLD sidecars, rotated affine georeferencing and optional PRJ. The first pixel read still decodes the complete compressed image; later reads can reuse caches.
- **Tile Core**: canonical north-origin XYZ coordinates internally, TMS conversion only at adapter boundaries, Web Mercator tile bounds, encoded payloads, byte-budgeted LRU cache and latest-request cancellation.
- **MBTiles**: metadata/tiles ingestion, physical TMS `tile_row` converted to canonical XYZ, PNG/JPEG/WebP/MVT payloads.
- **XYZ/TMS HTTP**: URL templates, timeout, caller cancellation, 404→null, transient retry, ETag/Last-Modified and caching.
- **WMS / WMTS**: WMS 1.3.0 GetMap baseline including latitude-first EPSG:4326 BBOX semantics; WMTS 1.0 KVP GetTile, TileMatrix templates and XYZ/TMS row handling.
- **MVT**: managed protobuf decoding for attributes and Point/MultiPoint/LineString/MultiLineString/Polygon/MultiPolygon, converted into existing `GisFeature` geometry in EPSG:3857.
- **PMTiles v3**: local random reads and HTTP Range, v3 header, Hilbert TileID, root/leaf directories, None/GZip/Brotli and MVT/PNG/JPEG/WebP. Zstd, AVIF, MapLibre Vector Tile and zoom >30 are explicit current boundaries.
- **Spatial indexing / raster viewport**: immutable packed R-tree, `IAsyncEnumerable<GisFeature>` streaming, raster window/overview cache and superseded-viewport cancellation.

## Principles

- **UI independent**: ingestion, CRS, geometry, raster, tile/network protocols and indexing must not depend on WinUI controls.
- **Preserve coordinate semantics**: CRS, axis order, X/Y/Z/M, raster pixel-center/corner semantics and XYZ/TMS row conventions must never be silently changed.
- **Separate data paths**: vector features, raster pixels, encoded tiles and WMS map images share domain foundations but keep distinct I/O and caching pipelines.
- **Reader isolation**: SQLite, LibTiff.NET, StbImageSharp and future GDAL/OGR or PROJ types stay behind adapters/services.
- **No fake remote random access**: remote COG and PMTiles require HTTP Range support; unsupported servers fail explicitly instead of triggering hidden full-file downloads.
- **Large-data ready**: GeoTIFF uses tile/strip window decoding and overviews; tile/network sources use byte-budgeted caches and cancellation. Real large-data and network stress benchmarks are reserved for Phase 6.
- **No compatibility overclaiming**: protocol versions, codecs, compression modes, capabilities parsing and extension boundaries are documented explicitly.
- **Independent versioning**: GisCore and the SpatialViewer UI evolve and release separately.

## Roadmap

See [`docs/ROADMAP.md`](docs/ROADMAP.md). See [`docs/COMPATIBILITY.md`](docs/COMPATIBILITY.md) for the current compatibility boundary.

## License

MIT License. See `THIRD-PARTY-NOTICES.md` for third-party notices.
