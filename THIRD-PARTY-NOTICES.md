# Third-Party Notices

SpatialViewer.GisCore keeps third-party runtime dependencies isolated in format/backend adapter projects. Public Core, projection, and rendering contracts expose only SpatialViewer-owned types.

## Runtime dependencies

### Microsoft.Data.Sqlite 10.0.11

Used only by `SpatialViewer.Formats.Gis.GeoPackage` for read-only GeoPackage/SQLite access. Microsoft.Data.Sqlite is distributed as part of the .NET data stack under its applicable open-source license. Its NuGet dependency graph may include SQLitePCLRaw/native SQLite components; their packaged license notices remain applicable to redistributed binaries.

The managed GeoPackage adapter parses GeoPackageBinary/WKB itself. It does not expose `SqliteConnection`, SQLite handles, or SQLite-specific value types through GisCore public APIs.

## Test dependencies

Test projects use Microsoft.NET.Test.Sdk and xUnit under their respective licenses.

## Future native GIS backends

GDAL/OGR, PROJ, NetTopologySuite, FileGDB drivers, or other format-specific libraries are **not** currently part of the Core public contract. If introduced, they must remain isolated behind adapters/services, be listed here with exact versions before release, and have native redistribution terms reviewed separately from NuGet package licenses.
