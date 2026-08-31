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
- CI, release workflow, architecture notes, compatibility matrix, and implementation roadmap.

### Changed
- Missing CRS remains explicitly `Unknown`; Phase 1 never invents EPSG:4326.
- Polygon rings and coordinate dimensions are validated without silently closing rings or discarding extra ordinates.

## [0.1.0] - Planned

Foundation milestone: stable core contracts plus first end-to-end GeoJSON read-to-render path.
