# SpatialViewer.GisCore

[中文](README.md) · [日本語](README.ja.md) · [English](README.en.md)

SpatialViewer 向けの独立 GIS ビューアーコアです。GIS データソースの読み込みアダプター、空間参照、ベクター/ラスター/タイルのドメインモデル、空間検索、キャッシュ/キャンセル、描画抽象化、回帰テストをこのリポジトリで管理します。WinUI 3 の製品 UI は `KiYouJyo/SpatialViewer` に残します。

> 現在の段階：**Phase 4 / 0.4.0 のタイル・ネットワークソース基盤を完了**しました。GeoJSON、Shapefile、GeoPackage、GeoTIFF、world image、CRS に加え、MBTiles、XYZ/TMS、WMS/WMTS、HTTP Range remote COG、managed MVT、PMTiles v3、共通 tile cache/cancellation を実装済みです。

## 現在の機能

- **GeoJSON**：主要 Geometry、Feature/FeatureCollection、属性、bbox、Z、CRS 欠落時の安全な挙動、RenderFrame 変換。
- **Shapefile**：SHP/SHX/DBF/PRJ/CPG、Point/MultiPoint/PolyLine/Polygon の 2D/Z/M、DBF 文字コード、PRJ CRS、範囲候補索引。
- **GeoPackage**：feature table、geometry column、属性、nullable geometry、GeoPackageBinary/WKB、Z/M、SRS_ID、ファイル内 RTree を利用した範囲検索。
- **CRS / Transform**：WKT1/WKT2 の EPSG authority 認識、未知 WKT の保持、明示的な軸順序ポリシー、EPSG:4326 ↔ EPSG:3857 変換。
- **GeoTIFF / local COG**：GeoKey、affine georeferencing、PixelIsArea/PixelIsPoint、nodata、band/color metadata、内部 overview、tile/strip window read。
- **Remote COG**：HTTP Range による tiled GeoTIFF / overview のランダム読み込み。サーバーは `206 Partial Content` を返す必要があり、Range を無視した HTTP 200 は全ファイルを暗黙に取得せず明示的に拒否します。
- **PNG/JPEG + world file**：PGW/JGW/長形式/WLD、回転 affine、任意の PRJ。最初の pixel read は圧縮画像全体をデコードしますが、その後は cache を再利用できます。
- **Tile Core**：内部座標は north-origin XYZ に統一し、TMS 変換は adapter 境界だけで実行。Web Mercator tile bounds、encoded payload、byte-budgeted LRU、latest-request cancellation を提供します。
- **MBTiles**：metadata/tiles、物理 TMS `tile_row` から canonical XYZ への変換、PNG/JPEG/WebP/MVT payload。
- **XYZ/TMS HTTP**：URL template、timeout、caller cancellation、404→null、一時的エラーの retry、ETag/Last-Modified、cache。
- **WMS / WMTS**：WMS 1.3.0 GetMap baseline（EPSG:4326 の緯度優先 BBOX を含む）、WMTS 1.0 KVP GetTile、TileMatrix template、XYZ/TMS row 処理。
- **MVT**：managed protobuf decoder で属性と Point/MultiPoint/LineString/MultiLineString/Polygon/MultiPolygon を読み、既存 `GisFeature` の EPSG:3857 geometry に変換します。
- **PMTiles v3**：local random read と HTTP Range、v3 header、Hilbert TileID、root/leaf directory、None/GZip/Brotli、MVT/PNG/JPEG/WebP。Zstd、AVIF、MapLibre Vector Tile、zoom >30 は現在明示的に未対応です。
- **空間索引 / Raster viewport**：immutable packed R-tree、`IAsyncEnumerable<GisFeature>` streaming、raster window/overview cache、不要になった viewport 読み込みの cancellation。

## 設計原則

- **UI 非依存**：読み込み、CRS、geometry、raster、tile/network protocol、索引は WinUI control に依存しません。
- **座標意味論を保持**：CRS、軸順序、X/Y/Z/M、pixel center/corner、XYZ/TMS row の意味を暗黙に変更しません。
- **データ経路を分離**：vector feature、raster pixel、encoded tile、WMS map image は基礎モデルを共有しつつ、I/O と cache pipeline を分けます。
- **reader 隔離**：SQLite、LibTiff.NET、StbImageSharp、将来の GDAL/OGR、PROJ 型を public API に露出しません。
- **remote random access を偽装しない**：remote COG と PMTiles は HTTP Range を必須とし、非対応サーバーで全ファイル download に暗黙退化しません。
- **大規模データ対応**：GeoTIFF は tile/strip window decode + overview、tile/network source は byte-budgeted cache + cancellation を利用します。実大型データ/ネットワーク stress benchmark は Phase 6 で検証します。
- **互換性を誇張しない**：protocol version、codec、compression、Capabilities parsing、extension の境界を compatibility matrix に明記します。
- **独立バージョン**：GisCore と SpatialViewer UI は別々にバージョン管理します。

## ロードマップ

[`docs/ROADMAP.md`](docs/ROADMAP.md) を参照してください。現在の互換範囲は [`docs/COMPATIBILITY.md`](docs/COMPATIBILITY.md) に記載しています。

## ライセンス

MIT License。第三者依存関係は `THIRD-PARTY-NOTICES.md` を参照してください。
