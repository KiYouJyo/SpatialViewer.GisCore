# SpatialViewer.GisCore

[中文](README.md) · [日本語](README.ja.md) · [English](README.en.md)

Independent GIS viewing core for SpatialViewer. This repository owns GIS data-source adapters, spatial-reference semantics, vector/raster models, spatial querying, rendering abstractions, and regression tests. The WinUI 3 product UI remains in `KiYouJyo/SpatialViewer`.

> Current stage: **Phase 2 / 0.2.0 vector formats and CRS baseline completed**. The managed baseline now covers GeoJSON, Shapefile, GeoPackage vector ingestion, explicit EPSG/WKT semantics and 4326↔3857 transforms, plus a backend-neutral packed R-tree. Phase 3 moves to GeoTIFF/COG and the raster viewing pipeline.

## Current capabilities

- **GeoJSON**: common Geometry families, Feature/FeatureCollection, properties, bbox, Z, safe missing-CRS behavior, and RenderFrame conversion.
- **Shapefile**: SHP/SHX/DBF/PRJ/CPG; 2D/Z/M Point/MultiPoint/PolyLine/Polygon; DBF encodings, PRJ CRS, and extent candidate indexing.
- **GeoPackage**: feature tables, geometry columns, attributes, nullable geometry, GeoPackageBinary/WKB, Z/M, SRS_ID validation, and file RTree-assisted extent queries.
- **CRS / Transform**: WKT1/WKT2 EPSG authority recognition, preservation of unknown WKT, explicit axis-order policy, and EPSG:4326 ↔ EPSG:3857 transforms.
- **Spatial indexing**: Core provides an immutable packed R-tree; readers keep `IAsyncEnumerable<GisFeature>` streaming contracts instead of requiring full-layer materialization.

## Principles

- **UI independent**: ingestion, CRS, geometry, raster, indexing, and scene conversion must not depend on WinUI controls.
- **Preserve coordinate semantics**: CRS, axis order, units, and source X/Y/Z/M coordinates must never be silently discarded.
- **Vector/raster separation**: both share document/layer concepts but keep distinct I/O, caching, and rendering pipelines.
- **Reader isolation**: SQLite and future GDAL/OGR, PROJ, NetTopologySuite, or other backend types stay behind adapters/services.
- **Large-data ready**: contracts support streaming, extent queries, cancellation, spatial indexes, and lazy loading; million-scale benchmarks are reserved for Phase 6.
- **Independent versioning**: GisCore and the SpatialViewer UI evolve and release separately.

## Roadmap

See [`docs/ROADMAP.md`](docs/ROADMAP.md). See [`docs/COMPATIBILITY.md`](docs/COMPATIBILITY.md) for the current compatibility boundary.

## License

MIT License. See `THIRD-PARTY-NOTICES.md` for third-party notices.
