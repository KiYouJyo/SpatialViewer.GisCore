# SpatialViewer.GisCore

[中文](README.md) · [日本語](README.ja.md) · [English](README.en.md)

Independent GIS viewing core for SpatialViewer. This repository owns GIS data-source adapters, spatial-reference semantics, vector/raster models, spatial querying, raster caching/cancellation, rendering abstractions, and regression tests. The WinUI 3 product UI remains in `KiYouJyo/SpatialViewer`.

> Current stage: **Phase 3 / 0.3.0 raster baseline completed**. In addition to GeoJSON, Shapefile, GeoPackage, CRS and spatial-index foundations, the managed baseline now includes GeoTIFF tile/strip window reads, internal overviews, PNG/JPEG + world files, a byte-budgeted raster cache, and superseded viewport cancellation. Phase 4 moves to MBTiles, XYZ/TMS, WMS/WMTS, remote COG and other tile/network sources.

## Current capabilities

- **GeoJSON**: common Geometry families, Feature/FeatureCollection, properties, bbox, Z, safe missing-CRS behavior, and RenderFrame conversion.
- **Shapefile**: SHP/SHX/DBF/PRJ/CPG; 2D/Z/M Point/MultiPoint/PolyLine/Polygon; DBF encodings, PRJ CRS, and extent candidate indexing.
- **GeoPackage**: feature tables, geometry columns, attributes, nullable geometry, GeoPackageBinary/WKB, Z/M, SRS_ID validation, and file RTree-assisted extent queries.
- **CRS / Transform**: WKT1/WKT2 EPSG authority recognition, preservation of unknown WKT, explicit axis-order policy, and EPSG:4326 ↔ EPSG:3857 transforms.
- **GeoTIFF**: GeoKey EPSG, ModelPixelScale/Tiepoint, ModelTransformation, PixelIsArea/PixelIsPoint, nodata, color/band metadata, internal overviews; viewport requests decode only intersecting tiles or strips.
- **PNG/JPEG + world file**: PGW/JGW/long-form/WLD sidecars, rotated affine georeferencing, optional same-name PRJ. The first PNG/JPEG pixel read still decodes the compressed image completely; decoded/viewport caches can then reuse it.
- **Raster viewport**: shared `RasterWindow` / `IRasterDataSourceReader`, byte-budgeted LRU cache, overview selection, and `RasterViewportReader`; a newer viewport cancels superseded raster work.
- **Spatial indexing**: Core provides an immutable packed R-tree; vector readers retain `IAsyncEnumerable<GisFeature>` streaming contracts instead of requiring full-layer materialization.

## Principles

- **UI independent**: ingestion, CRS, geometry, raster, indexing, and scene conversion must not depend on WinUI controls.
- **Preserve coordinate semantics**: CRS, axis order, units, source X/Y/Z/M coordinates, and raster pixel-center/corner semantics must never be silently discarded.
- **Vector/raster separation**: both share document/layer concepts but keep distinct I/O, caching, and rendering pipelines.
- **Reader isolation**: SQLite, LibTiff.NET, StbImageSharp, and future GDAL/OGR or PROJ types stay behind adapters/services.
- **Large-data ready**: GeoTIFF uses tile/strip window decoding and overviews; APIs support cancellation and caching. Real large-raster and million-feature benchmarks are reserved for Phase 6.
- **No compatibility overclaiming**: current COG support is a local tiled/overview-compatible path; remote HTTP Range COG is Phase 4. PNG/JPEG are not described as random-tile formats.
- **Independent versioning**: GisCore and the SpatialViewer UI evolve and release separately.

## Roadmap

See [`docs/ROADMAP.md`](docs/ROADMAP.md). See [`docs/COMPATIBILITY.md`](docs/COMPATIBILITY.md) for the current compatibility boundary.

## License

MIT License. See `THIRD-PARTY-NOTICES.md` for third-party notices.
