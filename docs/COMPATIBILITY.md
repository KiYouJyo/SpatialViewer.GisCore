# GIS Compatibility Matrix

| Capability | Stage | Current status / notes |
| --- | --- | --- |
| GeoJSON | P0 / 0.1 | **Phase 1 implemented**: FeatureCollection, Feature, direct Geometry; Point/MultiPoint/LineString/MultiLineString/Polygon/MultiPolygon/GeometryCollection; ids, nested properties, null geometry, bbox, Z, extent filtering and read-to-render conversion |
| Shapefile | P0 / 0.2 | **Phase 2 implemented (managed)**: SHP/SHX/DBF/PRJ/CPG; Point/MultiPoint/PolyLine/Polygon 2D/Z/M; DBF attributes/encoding; missing PRJ remains Unknown; extent queries use lightweight record bounds plus packed R-tree candidates |
| GeoPackage | P0 / 0.2 | **Phase 2 vector implemented**: feature tables, geometry columns, attributes, nullable geometry, GeoPackageBinary/WKB, Z/M, SRS_ID validation, layer metadata, and file RTree-assisted extent queries. Raster/tile tables remain later work |
| CRS / EPSG / WKT | P0 / 0.2 | **Phase 2 managed baseline implemented**: WKT1/WKT2 EPSG authority extraction, unknown WKT preservation, explicit axis-order policy, EPSG:4326 ↔ EPSG:3857 transform with Z/M preservation. Broader CRS coverage remains a future PROJ adapter concern |
| Spatial index / streaming | P0 / 0.2 | **Phase 2 implemented baseline**: immutable backend-neutral packed R-tree; Shapefile indexes record envelopes/references rather than full features; GeoPackage reuses native RTree tables when available; feature APIs remain async streaming |
| GeoTIFF / local COG-compatible TIFF | P0 / 0.3 | **Phase 3 implemented managed baseline**: GeoKey EPSG, ModelPixelScale/Tiepoint and ModelTransformation, PixelIsArea/PixelIsPoint, nodata, band/color metadata, internal overviews, tiled and stripped window reads. Tiled reads decode only intersecting tiles; strip reads decode only intersecting strips. Remote HTTP Range COG access is deferred to Phase 4 |
| PNG/JPEG + world file | P1 / 0.3 | **Phase 3 implemented**: PNG/JPEG metadata/pixel decode, PGW/JGW/long-form/WLD sidecars, rotated affine transforms, optional same-name PRJ, viewport resampling. PNG/JPEG are not random-tile containers: first pixel read decodes the complete compressed image, then weak-reference and viewport caches can reuse it |
| Raster viewport/cache | P0 / 0.3 | **Phase 3 implemented**: raster pixel-window contract, overview selector, byte-budgeted LRU cache, latest-request coordinator, `RasterViewportReader` integration, cancellation of superseded viewport reads |
| KML / KMZ / GPX | P1 / 0.4 | Planned interchange/track formats |
| MBTiles | P1 / 0.4 | Planned raster and vector tile containers |
| XYZ / TMS / WMS / WMTS | P1 / 0.4 | Planned network sources isolated behind service adapters |
| MVT / PMTiles | P1 / 0.4+ | Planned vector-tile path |
| FileGDB | P2 | Prefer future GDAL/OGR adapter; redistribution/license review required |
| PostGIS | P2 | Optional database adapter; not part of local-file MVP |

## Support boundary

“Implemented” in this matrix means the repository contains a concrete adapter/service and CI-backed regression coverage for the stated scope. It does not imply every vendor extension, TIFF photometric/layout combination, geometry variant, or CRS is accepted.

Core/public contracts remain independent from SQLite, LibTiff.NET, StbImageSharp, GDAL/OGR, and PROJ. Concrete dependencies stay inside adapters and may be replaced without exposing their handles or types to SpatialViewer UI code.

Phase 3's large-raster guarantee is specifically backed by the GeoTIFF tile/strip path: a requested viewport does not require construction of a whole-image RGBA buffer. PNG/JPEG are documented exceptions because their current managed decoder is whole-image on first decode. Remote COG/HTTP Range access remains a Phase 4 network-source task.

Million-scale vector benchmarks and genuinely large raster wall-clock/peak-memory/cache-pressure measurements remain reserved for the Phase 6 performance program rather than being inferred from small deterministic fixtures.
