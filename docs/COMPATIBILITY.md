# GIS Compatibility Matrix

| Capability | Stage | Current status / notes |
| --- | --- | --- |
| GeoJSON | P0 / 0.1 | **Phase 1 implemented**: FeatureCollection, Feature, direct Geometry; Point/MultiPoint/LineString/MultiLineString/Polygon/MultiPolygon/GeometryCollection; ids, nested properties, null geometry, bbox, Z, extent filtering and read-to-render conversion |
| Shapefile | P0 / 0.2 | **Phase 2 implemented (managed)**: SHP/SHX/DBF/PRJ/CPG; Point/MultiPoint/PolyLine/Polygon 2D/Z/M; DBF attributes/encoding; missing PRJ remains Unknown; extent queries use lightweight record bounds plus packed R-tree candidates |
| GeoPackage | P0 / 0.2 | **Phase 2 vector implemented**: feature tables, geometry columns, attributes, nullable geometry, GeoPackageBinary/WKB, Z/M, SRS_ID validation, layer metadata, and file RTree-assisted extent queries. Raster/tile tables remain later work |
| CRS / EPSG / WKT | P0 / 0.2 | **Phase 2 managed baseline implemented**: WKT1/WKT2 EPSG authority extraction, unknown WKT preservation, explicit axis-order policy, EPSG:4326 ↔ EPSG:3857 transform with Z/M preservation. Broader CRS coverage remains a future PROJ adapter concern |
| Spatial index / streaming | P0 / 0.2 | **Phase 2 implemented baseline**: immutable backend-neutral packed R-tree; Shapefile indexes record envelopes/references rather than full features; GeoPackage reuses native RTree tables when available; feature APIs remain async streaming |
| GeoTIFF / COG | P0 / 0.3 | Planned: georeferencing, nodata, overviews, tiled/windowed reads |
| PNG/JPEG + world file | P1 / 0.3 | Planned: sidecar georeferencing |
| KML / KMZ / GPX | P1 / 0.4 | Planned interchange/track formats |
| MBTiles | P1 / 0.4 | Planned raster and vector tile containers |
| XYZ / TMS / WMS / WMTS | P1 / 0.4 | Planned network sources isolated behind service adapters |
| MVT / PMTiles | P1 / 0.4+ | Planned vector-tile path |
| FileGDB | P2 | Prefer future GDAL/OGR adapter; redistribution/license review required |
| PostGIS | P2 | Optional database adapter; not part of local-file MVP |

## Support boundary

“Implemented” in this matrix means the repository contains a concrete adapter/service and CI-backed regression coverage for the stated scope. It does not imply every vendor extension or every geometry/CRS variant is accepted.

Phase 2 intentionally keeps the managed public contract independent from GDAL/OGR/PROJ. Future native backends may widen compatibility behind those contracts without exposing native handles or changing missing-CRS behavior.

Large-dataset architecture avoids mandatory full-feature materialization, but million-scale wall-clock and peak-memory benchmark claims are reserved for the Phase 6 performance program.
