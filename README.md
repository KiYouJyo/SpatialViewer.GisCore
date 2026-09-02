# SpatialViewer.GisCore

[中文](README.md) · [日本語](README.ja.md) · [English](README.en.md)

SpatialViewer 的独立 GIS 读图内核仓库。这里维护 GIS 数据源读取适配、空间参考、矢量/栅格/瓦片语义模型、空间查询、缓存/取消、渲染抽象与回归测试；WinUI 3 产品界面保留在 `KiYouJyo/SpatialViewer`。

> 当前阶段：**Phase 4 / 0.4.0 瓦片与网络数据源基线已完成**。在 GeoJSON、Shapefile、GeoPackage、GeoTIFF、world image 与 CRS 基础上，现已加入 MBTiles、XYZ/TMS、WMS/WMTS、HTTP Range remote COG、managed MVT 与 PMTiles v3，并建立统一 tile cache / cancellation 契约。

## 当前能力

- **GeoJSON**：完整常用 Geometry 家族、Feature/FeatureCollection、属性、bbox、Z、CRS 缺失安全行为与 RenderFrame 转换。
- **Shapefile**：SHP/SHX/DBF/PRJ/CPG；Point/MultiPoint/PolyLine/Polygon 的 2D/Z/M；DBF 编码、PRJ CRS、范围候选索引。
- **GeoPackage**：feature tables、geometry columns、属性、nullable geometry、GeoPackageBinary/WKB、Z/M、SRS_ID 与文件内 RTree 范围查询。
- **CRS / Transform**：WKT1/WKT2 EPSG authority 识别、未知 WKT 保留、显式轴顺序策略、EPSG:4326 ↔ EPSG:3857 转换。
- **GeoTIFF / local COG**：GeoKey、affine georeferencing、PixelIsArea/PixelIsPoint、nodata、band/color metadata、internal overview；窗口请求只解码相交 tile/strip。
- **Remote COG**：使用 HTTP Range 随机读取 tiled GeoTIFF 与 overview；必须获得 `206 Partial Content`，服务器忽略 Range 返回 200 时明确拒绝，不偷偷整文件下载。
- **PNG/JPEG + world file**：PGW/JGW/长扩展名/WLD、旋转 affine、可选 PRJ；首次像素读取仍需完整解压缩，之后可复用缓存。
- **Tile Core**：内部统一使用 north-origin XYZ，TMS 只在 adapter 边界翻转；提供 Web Mercator tile bounds、encoded payload、byte-budgeted LRU 与 latest-request cancellation。
- **MBTiles**：读取 metadata/tiles，物理 TMS `tile_row` 转换为 canonical XYZ，支持 PNG/JPEG/WebP/MVT payload。
- **XYZ/TMS HTTP**：URL 模板、超时、调用方取消、404→null、429/5xx/网络异常重试、ETag/Last-Modified 与缓存。
- **WMS / WMTS**：WMS 1.3.0 GetMap baseline（含 EPSG:4326 纬度优先 BBOX）；WMTS 1.0 KVP GetTile、TileMatrix 模板与 XYZ/TMS row 处理。
- **MVT**：managed protobuf 解码，属性与 Point/MultiPoint/LineString/MultiLineString/Polygon/MultiPolygon 转换为现有 `GisFeature`，tile-local 坐标转换为 EPSG:3857。
- **PMTiles v3**：本地随机读取与 HTTP Range；v3 header、Hilbert TileID、root/leaf directory，支持 None/GZip/Brotli 与 MVT/PNG/JPEG/WebP。Zstd、AVIF、MapLibre Vector Tile 与 zoom>30 当前明确不支持。
- **空间索引 / Raster viewport**：immutable packed R-tree、`IAsyncEnumerable<GisFeature>`、raster window/overview cache 与过期视口取消。

## 设计原则

- **UI 无关**：读取、CRS、几何、栅格、瓦片、网络协议与索引不得依赖 WinUI 3 页面或控件。
- **坐标语义优先**：CRS、轴顺序、X/Y/Z/M、像素中心/外框、XYZ/TMS 行方向都必须显式处理，不在 adapter 中悄悄改义。
- **按数据类型分流**：vector、raster、encoded tile、WMS map image 共享基础域模型，但保留各自读取/缓存路径。
- **读取器隔离**：SQLite、LibTiff.NET、StbImageSharp 及未来 GDAL/OGR、PROJ 等实现只能存在于 adapter/service 层，不向公共 API 泄漏类型。
- **远程随机访问不造假**：remote COG 与 PMTiles 必须使用 HTTP Range；服务器不支持 Range 时显式失败，不退化为隐藏的整文件下载。
- **大数据友好**：GeoTIFF 采用 tile/strip window + overview；tile/network source 使用 byte-budgeted cache 与 cancellation。真实大型数据与网络压力 benchmark 在 Phase 6 单独验收。
- **不夸大兼容性**：协议版本、编码、压缩、Capabilities/扩展支持边界写入兼容矩阵；未实现内容不以“兼容”名义带过。
- **独立版本**：GIS 内核与 SpatialViewer UI 分别版本化，通过明确依赖版本集成。

## 仓库边界

本仓库是 GIS 读图内核的源代码归属。`SpatialViewer` 负责窗口、标签页、工具栏、图层面板和用户交互，只通过稳定接口使用本仓库提供的能力。

## 路线图

见 [`docs/ROADMAP.md`](docs/ROADMAP.md)。当前兼容范围见 [`docs/COMPATIBILITY.md`](docs/COMPATIBILITY.md)。

## 许可证

MIT License。第三方依赖及许可信息见 `THIRD-PARTY-NOTICES.md`。
