# Third-Party Notices

SpatialViewer.GisCore keeps third-party runtime dependencies isolated in format/backend adapter projects. Public Core, projection, rendering, and data-source contracts expose only SpatialViewer-owned types.

## Runtime dependencies

### Microsoft.Data.Sqlite 10.0.11

Used only by `SpatialViewer.Formats.Gis.GeoPackage` for read-only GeoPackage/SQLite access. Microsoft.Data.Sqlite is distributed as part of the .NET data stack under its applicable open-source license. Its NuGet dependency graph may include SQLitePCLRaw/native SQLite components; their packaged license notices remain applicable to redistributed binaries.

The managed GeoPackage adapter parses GeoPackageBinary/WKB itself. It does not expose `SqliteConnection`, SQLite handles, or SQLite-specific value types through GisCore public APIs.

### BitMiracle.LibTiff.NET 2.4.660

Used only by `SpatialViewer.Formats.Gis.GeoTiff` for TIFF directory/tag access and compressed tile/strip RGBA decoding. The package is distributed under the New BSD License. Copyright and license terms from Bit Miracle / LibTiff.NET remain applicable to redistributed binaries.

GisCore does not expose `Tiff`, `TiffTag`, `FieldValue`, or other LibTiff.NET types through its public contracts. GeoTIFF/GeoKey interpretation and the raster window abstraction remain SpatialViewer-owned code.

### StbImageSharp 2.30.16

Used only by `SpatialViewer.Formats.Gis.WorldImage` for managed PNG/JPEG metadata and pixel decoding. NuGet identifies the package under the Unlicense OR MIT license. StbImageSharp is a C# port of `stb_image` and does not require a native image-decoder binary.

GisCore exposes only `RasterReadResult` and related SpatialViewer-owned metadata; StbImageSharp result/component types remain inside the WorldImage adapter.

## Test dependencies

Test projects use Microsoft.NET.Test.Sdk and xUnit under their respective licenses. Synthetic raster fixtures were generated specifically for GisCore regression testing and contain no third-party map imagery or proprietary GIS datasets.

## Future native GIS backends

GDAL/OGR, PROJ, NetTopologySuite, FileGDB drivers, or other format-specific libraries are **not** currently part of the Core public contract. If introduced, they must remain isolated behind adapters/services, be listed here with exact versions before release, and have native redistribution terms reviewed separately from NuGet package licenses.
