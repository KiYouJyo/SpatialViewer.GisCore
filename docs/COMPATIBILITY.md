# GIS Compatibility Matrix

| Capability | Stage | Notes |
| --- | --- | --- |
| GeoJSON | P0 / 0.1 | First managed reference adapter |
| Shapefile | P0 / 0.2 | Reader adapter; DBF encoding and PRJ handling required |
| GeoPackage | P0 / 0.2 | Vector first; raster/tile tables later |
| CRS / EPSG / WKT | P0 / 0.2 | Explicit transform service; never assume missing CRS |
| GeoTIFF / COG | P0 / 0.3 | Georeferencing, nodata, overviews, tiled reads |
| PNG/JPEG + world file | P1 / 0.3 | Sidecar georeferencing |
| KML / KMZ / GPX | P1 / 0.4 | Interchange/track formats |
| MBTiles | P1 / 0.4 | Raster and vector tile containers |
| XYZ / TMS / WMS / WMTS | P1 / 0.4 | Network sources isolated behind service adapters |
| MVT / PMTiles | P1 / 0.4+ | Vector-tile path |
| FileGDB | P2 | Prefer GDAL/OGR adapter; redistribution/license review required |
| PostGIS | P2 | Optional database adapter; not part of local-file MVP |

The matrix describes implementation priority, not current support. Only features covered by tests and marked complete in the changelog may be advertised as supported.
