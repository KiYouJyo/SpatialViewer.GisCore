# SpatialViewer.GisCore

[中文](README.md) · [日本語](README.ja.md) · [English](README.en.md)

SpatialViewer 向けの独立 GIS ビューアーコアです。GIS データソースの読み込みアダプター、空間参照、ベクター/ラスターの意味モデル、空間検索、描画抽象化、回帰テストをこのリポジトリで管理します。WinUI 3 の製品 UI は `KiYouJyo/SpatialViewer` に残します。

> 現在の段階：**Phase 2 / 0.2.0 のベクター形式・CRS 基盤を完了**しました。managed baseline は GeoJSON、Shapefile、GeoPackage のベクター読み込み、明示的な EPSG/WKT セマンティクスと 4326↔3857 座標変換、backend-neutral packed R-tree を実装済みです。Phase 3 では GeoTIFF/COG を中心とするラスター読み込みへ進みます。

## 現在の機能

- **GeoJSON**：主要 Geometry、Feature/FeatureCollection、属性、bbox、Z、CRS 欠落時の安全な挙動、RenderFrame 変換。
- **Shapefile**：SHP/SHX/DBF/PRJ/CPG、Point/MultiPoint/PolyLine/Polygon の 2D/Z/M、DBF 文字コード、PRJ CRS、範囲候補索引。
- **GeoPackage**：feature table、geometry column、属性、nullable geometry、GeoPackageBinary/WKB、Z/M、SRS_ID、ファイル内 RTree を利用した範囲検索。
- **CRS / Transform**：WKT1/WKT2 の EPSG authority 認識、未知 WKT の保持、明示的な軸順序ポリシー、EPSG:4326 ↔ EPSG:3857 変換。
- **空間索引**：Core に immutable packed R-tree を実装し、reader は `IAsyncEnumerable<GisFeature>` のストリーミング契約を維持します。

## 設計原則

- **UI 非依存**：読み込み、CRS、ジオメトリ、ラスター、索引、シーン変換は WinUI コントロールに依存しません。
- **座標意味論を保持**：CRS、軸順序、単位、元の X/Y/Z/M 座標を暗黙に失わないこと。
- **ベクター/ラスター分離**：文書・レイヤー概念は共有しつつ、I/O、キャッシュ、描画経路を分離します。
- **リーダー隔離**：SQLite、および将来の GDAL/OGR、PROJ、NetTopologySuite などの型を上位層へ露出しません。
- **大規模データ対応**：ストリーミング、範囲検索、キャンセル、空間索引、遅延読み込みを前提にします。百万規模の benchmark は Phase 6 で別途検証します。
- **独立バージョン**：GisCore と SpatialViewer UI は別々にバージョン管理します。

## ロードマップ

[`docs/ROADMAP.md`](docs/ROADMAP.md) を参照してください。現在の互換範囲は [`docs/COMPATIBILITY.md`](docs/COMPATIBILITY.md) に記載しています。

## ライセンス

MIT License。第三者依存関係は `THIRD-PARTY-NOTICES.md` を参照してください。
