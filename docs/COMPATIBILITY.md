# GIS Compatibility Matrix

| Capability | Stage | Current status / notes |
| --- | --- | --- |
| GeoJSON | P0 / 0.1 | **Phase 1 implemented**: FeatureCollection, Feature, direct Geometry; Point/MultiPoint/LineString/MultiLineString/Polygon/MultiPolygon/GeometryCollection; ids, nested properties, null geometry, bbox, Z, extent filtering and read-to-render conversion |
| Shapefile | P0 / 0.2 | Reader adapter; DBF encoding and PRJ handling required |
| GeoPackage | P0 / 0.2 | Vector first; raster/tile tables later |
| CRS / EPSG / WKT | P0 / 0.2 | Explicit transform service; never assume missing CRS. Phase 1 only preserves explicitly declared legacy GeoJSON CRS names |
| GeoTIFF / COG | P0 / 0.3 | Georeferencing, nodata, overviews, tiled reads |
| PNG/JPEG + world file | P1 / 0.3 | Sidecar georeferencing |
| KML / KMZ / GPX | P1 / 0.4 | Interchange/track formats |
| MBTiles | P1 / 0.4 | Raster and vector tile containers |
| XYZ / TMS / WMS / WMTS | P1 / 0.4 | Network sources isolated behind service adapters |
| MVT / PMTiles | P1 / 0.4+ | Vector-tile path |
| FileGDB | P2 | Prefer GDAL/OGR adapter; redistribution/license review required |
| PostGIS | P2 | Optional database adapter; not part of local-file MVP |

The matrix describes implementation priority and tested support. Capabilities may be advertised only after their CI-backed tests are green on the release candidate.
