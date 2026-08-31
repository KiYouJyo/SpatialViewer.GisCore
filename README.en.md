# SpatialViewer.GisCore

[中文](README.md) · [日本語](README.ja.md) · [English](README.en.md)

Independent GIS viewing core for SpatialViewer. This repository owns GIS data-source adapters, spatial-reference semantics, vector/raster models, spatial querying, rendering abstractions, and regression tests. The WinUI 3 product UI remains in `KiYouJyo/SpatialViewer`.

> Current phase: establish the same independent build/test boundary used by `SpatialViewer.CadCore`, then implement GIS primitives and adapter contracts before adding GeoJSON, Shapefile, GeoPackage, GeoTIFF, and other readers.

## Principles

- **UI independent**: ingestion, CRS, geometry, raster, indexing, and scene conversion must not depend on WinUI controls.
- **Preserve coordinate semantics**: CRS, axis order, units, and source coordinates must never be silently discarded.
- **Vector/raster separation**: both share document/layer concepts but keep distinct I/O, caching, and rendering pipelines.
- **Reader isolation**: GDAL/OGR, PROJ, NetTopologySuite, or other third-party types stay behind adapters.
- **Large-data ready**: contracts support streaming, extent queries, cancellation, spatial indexes, and lazy loading.
- **Independent versioning**: GisCore and the SpatialViewer UI evolve and release separately.

## Roadmap

See [`docs/ROADMAP.md`](docs/ROADMAP.md).

## License

MIT License. See `THIRD-PARTY-NOTICES.md` for third-party notices.
