# SpatialViewer.GisCore

[中文](README.md) · [日本語](README.ja.md) · [English](README.en.md)

SpatialViewer 向けの独立 GIS ビューアーコアです。GIS データソースの読み込みアダプター、空間参照、ベクター/ラスターの意味モデル、空間検索、ラスターキャッシュ/キャンセル、描画抽象化、回帰テストをこのリポジトリで管理します。WinUI 3 の製品 UI は `KiYouJyo/SpatialViewer` に残します。

> 現在の段階：**Phase 3 / 0.3.0 のラスター基盤を完了**しました。GeoJSON、Shapefile、GeoPackage、CRS、空間索引に加え、GeoTIFF の tile/strip ウィンドウ読み込み、内部 overview、PNG/JPEG + world file、バイト上限付きラスターキャッシュ、古い viewport 要求のキャンセルを実装済みです。Phase 4 では MBTiles、XYZ/TMS、WMS/WMTS、remote COG などのタイル/ネットワークソースへ進みます。

## 現在の機能

- **GeoJSON**：主要 Geometry、Feature/FeatureCollection、属性、bbox、Z、CRS 欠落時の安全な挙動、RenderFrame 変換。
- **Shapefile**：SHP/SHX/DBF/PRJ/CPG、Point/MultiPoint/PolyLine/Polygon の 2D/Z/M、DBF 文字コード、PRJ CRS、範囲候補索引。
- **GeoPackage**：feature table、geometry column、属性、nullable geometry、GeoPackageBinary/WKB、Z/M、SRS_ID、ファイル内 RTree を利用した範囲検索。
- **CRS / Transform**：WKT1/WKT2 の EPSG authority 認識、未知 WKT の保持、明示的な軸順序ポリシー、EPSG:4326 ↔ EPSG:3857 変換。
- **GeoTIFF**：GeoKey EPSG、ModelPixelScale/Tiepoint、ModelTransformation、PixelIsArea/PixelIsPoint、nodata、color/band metadata、内部 overview。viewport では交差する tile/strip のみをデコードします。
- **PNG/JPEG + world file**：PGW/JGW/長形式/WLD、回転 affine georeferencing、同名 PRJ。PNG/JPEG は最初のピクセル読み込み時に圧縮画像全体をデコードしますが、その後は decoded/viewport cache を再利用できます。
- **Raster viewport**：共通 `RasterWindow` / `IRasterDataSourceReader`、バイト上限付き LRU cache、overview selector、`RasterViewportReader`。新しい viewport 要求は不要になった古い読み込みをキャンセルします。
- **空間索引**：Core に immutable packed R-tree を実装し、vector reader は `IAsyncEnumerable<GisFeature>` のストリーミング契約を維持します。

## 設計原則

- **UI 非依存**：読み込み、CRS、ジオメトリ、ラスター、索引、シーン変換は WinUI コントロールに依存しません。
- **座標意味論を保持**：CRS、軸順序、単位、元の X/Y/Z/M、さらにラスターの pixel-center/corner 意味を暗黙に失わないこと。
- **ベクター/ラスター分離**：文書・レイヤー概念は共有しつつ、I/O、キャッシュ、描画経路を分離します。
- **リーダー隔離**：SQLite、LibTiff.NET、StbImageSharp、および将来の GDAL/OGR、PROJ の型を上位層へ露出しません。
- **大規模データ対応**：GeoTIFF は tile/strip の window decode と overview を利用し、API はキャンセル/キャッシュに対応します。実大型ラスターおよび百万規模 vector benchmark は Phase 6 で別途検証します。
- **互換性を誇張しない**：現在の COG はローカル tiled/overview 互換経路であり、HTTP Range remote COG は Phase 4 です。PNG/JPEG も random-tile 形式とは扱いません。
- **独立バージョン**：GisCore と SpatialViewer UI は別々にバージョン管理します。

## ロードマップ

[`docs/ROADMAP.md`](docs/ROADMAP.md) を参照してください。現在の互換範囲は [`docs/COMPATIBILITY.md`](docs/COMPATIBILITY.md) に記載しています。

## ライセンス

MIT License。第三者依存関係は `THIRD-PARTY-NOTICES.md` を参照してください。
