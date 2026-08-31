# Changelog

All notable changes to SpatialViewer.GisCore will be documented in this file.

## [Unreleased]

### Added
- Initial repository scaffold aligned with SpatialViewer.CadCore conventions.
- GIS domain primitives for coordinates, extents, spatial references, features, and layers.
- Complete managed Phase 1 GeoJSON reader for FeatureCollection, Feature and direct Geometry roots.
- Point, MultiPoint, LineString, MultiLineString, Polygon, MultiPolygon and GeometryCollection semantic models with nullable/empty geometry handling.
- Feature ids, nested JSON attributes, null values, declared 2D/3D bbox, Z coordinates and explicit legacy named CRS preservation.
- Extent filtering and backend-neutral vector RenderFrame conversion with typed Point/Polyline/Polygon payloads.
- Synthetic malformed and mixed-geometry regression fixtures plus generated 4096-feature stress coverage.
- Managed Shapefile reader for SHP/SHX/DBF/PRJ/CPG with Point/MultiPoint/PolyLine/Polygon 2D/Z/M, DBF encoding, projection metadata, and record-index validation.
- Managed GeoPackage vector reader for feature tables, geometry columns, attributes, nullable geometry, GeoPackageBinary/WKB, Z/M, SRS_ID validation, and RTree-assisted extent queries.
- Explicit projection service with WKT1/WKT2 EPSG authority parsing, axis-order policy, and tested EPSG:4326 ↔ EPSG:3857 transforms preserving Z/M.
- Backend-neutral immutable packed R-tree plus lightweight Shapefile record-envelope candidate filtering.
- CI, release workflow, architecture notes, compatibility matrix, and implementation roadmap.

### Changed
- Missing CRS remains explicitly `Unknown`; readers never invent EPSG:4326.
- Polygon rings and coordinate dimensions are validated without silently closing rings or discarding extra ordinates.
- `GisCoordinate` preserves an optional M ordinate in addition to X/Y/Z.
- Phase 2 projects are explicit solution members so both Debug and Release configurations build Projections, Shapefile, and GeoPackage directly.
- GeoPackage connections are read-only, private-cache, and non-pooled to avoid hidden file-handle retention.

## [0.2.0] - Planned

Phase 2 implementation milestone: GeoJSON + Shapefile + GeoPackage vector ingestion, managed CRS baseline, explicit coordinate transforms, and spatial-index foundations. Release/tag remains pending merge and release workflow validation.

## [0.1.0] - Planned

Foundation milestone: stable core contracts plus first end-to-end GeoJSON read-to-render path.
