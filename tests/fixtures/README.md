# GIS Test Fixtures

Only add datasets that are small, deterministic, and legally redistributable.

For every fixture, record the source/license and the behavior being tested. Prefer synthetic fixtures generated specifically for regression tests. Do not commit proprietary GIS datasets, credentials, API tokens, map imagery, or large production extracts.

## Phase 1 GeoJSON fixtures

All files under `geojson/` are synthetic and authored for this repository under the repository license.

- `all-geometries.geojson`: all seven GeoJSON geometry families, null geometry, nested attributes, ids, bbox and Z.
- `legacy-crs.geojson`: explicit legacy named CRS handling.
- `malformed-open-ring.geojson`: verifies invalid rings are diagnosed instead of auto-closed.
- `malformed-json.geojson`: verifies JSON parse errors include an actionable source path.
- Large-file coverage is generated deterministically at test runtime (4096 features) so the repository does not carry a bulky production-like dataset.

## Phase 2 Shapefile / GeoPackage fixtures

Shapefile files under `shapefile/` are synthetic and authored specifically for the repository. They cover SHP/SHX/DBF/PRJ/CPG coordination, Point Z/M, multipart PolyLine Z/M, attributes, encoding, and missing-PRJ behavior.

GeoPackage regression databases are generated at test runtime with Microsoft.Data.Sqlite so the repository does not commit opaque database blobs. Generated tables cover spatial reference metadata, Point/LineString Z/M, attributes, nullable geometry, GeoPackageBinary/WKB, and RTree queries.

## Phase 3 raster fixtures

All raster pixels are synthetic color ramps generated specifically for GisCore; they contain no third-party basemap or aerial imagery.

- `geotiff/phase3-tiled-overview.tif`: 32×32 RGB Deflate TIFF, 16×16 tiling, EPSG:3857 GeoKeys, ModelPixelScale/ModelTiepoint, nodata, and a 16×16 internal reduced-image overview. It verifies tiled window orientation, georeferencing and overview selection.
- `geotiff/phase3-strip.tif`: 12×10 RGB Deflate TIFF with rows-per-strip=4 and EPSG:3857 georeferencing. It verifies that the stripped reader path returns correct top-left-oriented window pixels.
- `geotiff/pixel-is-point.tif`: 4×3 RGB GeoTIFF with `RasterPixelIsPoint`; it verifies the half-pixel normalization from source pixel-center semantics to the Core pixel-corner affine/bounds model while retaining the Point anchor metadata.
- `world-image/rotated.png` + `rotated.pgw` + `rotated.prj`: 12×10 synthetic RGBA PNG with a rotated world-file affine transform and EPSG:4326 PRJ.
- `world-image/photo.jpg` + `photo.jgw` + `photo.prj`: 12×10 synthetic JPEG using the same affine/CRS semantics. Pixel-value assertions intentionally use the lossless PNG; JPEG tests validate decode/metadata/sidecar integration without assuming lossless samples.
