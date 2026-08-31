# GIS Test Fixtures

Only add datasets that are small, deterministic, and legally redistributable.

For every fixture, record the source/license and the behavior being tested. Prefer synthetic fixtures generated specifically for regression tests. Do not commit proprietary GIS datasets, credentials, API tokens, or large production extracts.

## Phase 1 GeoJSON fixtures

All files under `geojson/` are synthetic and authored for this repository under the repository license.

- `all-geometries.geojson`: all seven GeoJSON geometry families, null geometry, nested attributes, ids, bbox and Z.
- `legacy-crs.geojson`: explicit legacy named CRS handling.
- `malformed-open-ring.geojson`: verifies invalid rings are diagnosed instead of auto-closed.
- `malformed-json.geojson`: verifies JSON parse errors include an actionable source path.
- Large-file coverage is generated deterministically at test runtime (4096 features) so the repository does not carry a bulky production-like dataset.
