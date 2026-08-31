# コントリビューション

1. `main` からブランチを作成し、1 つの PR は 1 つの明確な課題に集中させてください。
2. `dotnet build SpatialViewer.GisCore.sln -c Release` と `dotnet test SpatialViewer.GisCore.sln -c Release` を実行します。
3. 形式、CRS、ジオメトリ、描画挙動を変更する場合はテストを追加してください。再配布可能なサンプルは `tests/fixtures` に置きます。
4. 第三者 GIS ライブラリは adapter プロジェクトに隔離し、Core/Rendering API に型を露出しないでください。
5. Native 依存や再配布条件が変わる場合は `THIRD-PARTY-NOTICES.md` を更新します。
