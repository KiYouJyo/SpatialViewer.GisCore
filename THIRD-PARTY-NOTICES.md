# Third-Party Notices

SpatialViewer.GisCore keeps third-party runtime dependencies isolated in format/backend adapter projects. Public Core, projection, rendering, and data-source contracts expose only SpatialViewer-owned types.

## Runtime dependencies

### Microsoft.Data.Sqlite 10.0.11

Used by `SpatialViewer.Formats.Gis.GeoPackage` and `SpatialViewer.Formats.Gis.MbTiles` for read-only SQLite-backed GIS data access. Microsoft.Data.Sqlite is distributed as part of the .NET data stack under its applicable open-source license. Its NuGet dependency graph may include SQLitePCLRaw/native SQLite components; their packaged license notices remain applicable to redistributed binaries.

GeoPackage and MBTiles adapters do not expose `SqliteConnection`, SQLite handles, or SQLite-specific value types through GisCore public APIs. Local read paths use private caches with pooling disabled where file replacement/deletion semantics matter.

### BitMiracle.LibTiff.NET 2.4.660

Used by `SpatialViewer.Formats.Gis.GeoTiff` for local TIFF directory/tag access and compressed tile/strip RGBA decoding, and by `SpatialViewer.Formats.Gis.RemoteCog` behind an HTTP Range-backed `TiffStream` for remote tiled GeoTIFF access. The package is distributed under the New BSD License. Copyright and license terms from Bit Miracle / LibTiff.NET remain applicable to redistributed binaries.

GisCore does not expose `Tiff`, `TiffTag`, `FieldValue`, `TiffStream`, or other LibTiff.NET types through public contracts. GeoTIFF/GeoKey interpretation, raster windows, HTTP Range policy, and cache semantics remain SpatialViewer-owned code.

### StbImageSharp 2.30.16

Used only by `SpatialViewer.Formats.Gis.WorldImage` for managed PNG/JPEG metadata and pixel decoding. NuGet identifies the package under the Unlicense OR MIT license. StbImageSharp is a C# port of `stb_image` and does not require a native image-decoder binary.

GisCore exposes only `RasterReadResult` and related SpatialViewer-owned metadata; StbImageSharp result/component types remain inside the WorldImage adapter.

## Managed format/protocol implementations without added runtime libraries

### Mapbox Vector Tile

`SpatialViewer.Formats.Gis.Mvt` contains a SpatialViewer-owned managed Protocol Buffer/MVT reader for the Phase 4 data-decoding scope. No protobuf runtime package or Mapbox SDK is a runtime dependency. The decoder emits existing SpatialViewer-owned `GisFeature` / `IGisGeometry` types.

### PMTiles v3

`SpatialViewer.Formats.Gis.PmTiles` contains a SpatialViewer-owned PMTiles v3 parser for local random access and HTTP Range. No PMTiles reference implementation or SDK is linked as a runtime dependency. The PMTiles specification is published as a public-domain/CC0 specification; any separately consulted reference implementation retains its own license and is not redistributed by this repository.

The managed adapter uses .NET BCL compression for None/GZip/Brotli. Zstandard support is not bundled in 0.4.0 and therefore adds no native/runtime dependency.

### HTTP tile and web-map protocols

XYZ/TMS, WMS, WMTS, remote COG Range transport and remote PMTiles Range transport use the .NET BCL `HttpClient` stack. No third-party HTTP/network SDK is required. Public contracts expose URI/source strings and SpatialViewer-owned result models rather than `HttpClient` or handler types.

## Test dependencies

Test projects use Microsoft.NET.Test.Sdk and xUnit under their respective licenses. Synthetic vector/raster/tile/PMTiles fixtures and deterministic custom HTTP handlers were created specifically for GisCore regression testing and contain no third-party map imagery or proprietary GIS datasets.

## Future native GIS backends

GDAL/OGR, PROJ, NetTopologySuite, FileGDB drivers, Zstandard libraries, or other format-specific libraries are **not** currently part of the Core public contract. If introduced, they must remain isolated behind adapters/services, be listed here with exact versions before release, and have native redistribution terms reviewed separately from NuGet package licenses.
