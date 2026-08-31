# Contributing

1. Branch from `main` and keep each pull request focused.
2. Run `dotnet build SpatialViewer.GisCore.sln -c Release` and `dotnet test SpatialViewer.GisCore.sln -c Release`.
3. Add tests for format, CRS, geometry, or rendering behavior changes. Redistributable samples belong under `tests/fixtures`.
4. Third-party GIS libraries must stay in adapter projects and must not leak public types into Core/Rendering APIs.
5. Update `THIRD-PARTY-NOTICES.md` when native dependencies or redistribution terms change.
