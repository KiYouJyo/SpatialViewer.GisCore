# SpatialViewer.GisCore

[中文](README.md) · [日本語](README.ja.md) · [English](README.en.md)

SpatialViewer 向けの独立 GIS ビューアーコアです。GIS データソースの読み込みアダプター、空間参照、ベクター/ラスターの意味モデル、空間検索、描画抽象化、回帰テストをこのリポジトリで管理します。WinUI 3 の製品 UI は `KiYouJyo/SpatialViewer` に残します。

> 現在の段階：`SpatialViewer.CadCore` と同じ独立ビルド/テスト境界を作り、GIS 基礎モデルとアダプター契約を整えた後、GeoJSON、Shapefile、GeoPackage、GeoTIFF などを段階的に実装します。

## 設計原則

- **UI 非依存**：読み込み、CRS、ジオメトリ、ラスター、索引、シーン変換は WinUI コントロールに依存しません。
- **座標意味論を保持**：CRS、軸順序、単位、元座標を暗黙に失わないこと。
- **ベクター/ラスター分離**：文書・レイヤー概念は共有しつつ、I/O、キャッシュ、描画経路を分離します。
- **リーダー隔離**：GDAL/OGR、PROJ、NetTopologySuite などの型を上位層へ露出しません。
- **大規模データ対応**：ストリーミング、範囲検索、キャンセル、空間索引、遅延読み込みを前提にします。
- **独立バージョン**：GisCore と SpatialViewer UI は別々にバージョン管理します。

## ロードマップ

[`docs/ROADMAP.md`](docs/ROADMAP.md) を参照してください。

## ライセンス

MIT License。第三者依存関係は `THIRD-PARTY-NOTICES.md` を参照してください。
