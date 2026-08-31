# SpatialViewer.GisCore

[中文](README.md) · [日本語](README.ja.md) · [English](README.en.md)

SpatialViewer 的独立 GIS 读图内核仓库。这里维护 GIS 数据源读取适配、空间参考、矢量/栅格语义模型、空间查询、缓存/取消、渲染抽象与回归测试；WinUI 3 产品界面保留在 `KiYouJyo/SpatialViewer`。

> 当前阶段：**Phase 3 / 0.3.0 栅格读图基线已完成**。在 GeoJSON、Shapefile、GeoPackage 与 CRS/空间索引基础上，现已加入 GeoTIFF tile/strip 窗口读取、内部 overview、PNG/JPEG + world file、栅格 LRU cache 与视口请求取消。下一阶段进入 MBTiles、XYZ/TMS、WMS/WMTS、remote COG 等瓦片/网络数据源。

## 当前能力

- **GeoJSON**：完整常用 Geometry 家族、Feature/FeatureCollection、属性、bbox、Z、CRS 缺失安全行为与 RenderFrame 转换。
- **Shapefile**：SHP/SHX/DBF/PRJ/CPG；Point/MultiPoint/PolyLine/Polygon 的 2D/Z/M；DBF 编码、PRJ CRS、范围候选索引。
- **GeoPackage**：feature tables、geometry columns、属性、nullable geometry、GeoPackageBinary/WKB、Z/M、SRS_ID 与文件内 RTree 范围查询。
- **CRS / Transform**：WKT1/WKT2 EPSG authority 识别、未知 WKT 保留、显式轴顺序策略、EPSG:4326 ↔ EPSG:3857 转换。
- **GeoTIFF**：GeoKey EPSG、ModelPixelScale/Tiepoint、ModelTransformation、PixelIsArea/PixelIsPoint、nodata、颜色/band metadata、内部 overview；窗口请求只解码相交 tile 或 strip。
- **PNG/JPEG + world file**：PGW/JGW/长扩展名/WLD、旋转 affine georeferencing、可选同名 PRJ；PNG/JPEG 首次像素读取仍需完整解压缩，之后可复用解码/视口缓存。
- **Raster viewport**：统一 `RasterWindow` / `IRasterDataSourceReader`，byte-budgeted LRU cache、overview selector、`RasterViewportReader`，新视口会取消已被替代的旧读取。
- **空间索引**：Core 提供 immutable packed R-tree；矢量读取保持 `IAsyncEnumerable<GisFeature>`，不要求整层完整物化。

## 设计原则

- **UI 无关**：数据读取、CRS、几何、栅格、索引与场景转换不得依赖 WinUI 3 页面或控件。
- **坐标语义优先**：数据源 CRS、轴顺序、单位与原始 X/Y/Z/M 信息不得在导入阶段静默丢失；栅格像素中心/外框语义也必须显式正规化。
- **矢量/栅格分流**：两类数据共享文档与图层抽象，但拥有各自的读取、缓存和渲染路径。
- **读取器隔离**：SQLite、LibTiff.NET、StbImageSharp 以及未来的 GDAL/OGR、PROJ 等实现只能存在于适配/服务层，不向上层公开第三方类型。
- **大数据友好**：GeoTIFF 采用 tile/strip 窗口解码与 overview；接口支持取消与缓存。真实大型栅格/百万级矢量 benchmark 在 Phase 6 单独验收。
- **不夸大兼容性**：当前 COG 是本地 tiled/overview 兼容路径；HTTP Range remote COG 属于 Phase 4。PNG/JPEG 也不会被描述成随机块读取格式。
- **独立版本**：GIS 内核与 SpatialViewer UI 分别版本化，通过明确依赖版本集成。

## 仓库边界

本仓库是 GIS 读图内核的源代码归属。`SpatialViewer` 负责窗口、标签页、工具栏、图层面板和用户交互，只通过稳定接口使用本仓库提供的能力。

## 路线图

见 [`docs/ROADMAP.md`](docs/ROADMAP.md)。当前兼容范围见 [`docs/COMPATIBILITY.md`](docs/COMPATIBILITY.md)。

## 许可证

MIT License。第三方依赖及许可信息见 `THIRD-PARTY-NOTICES.md`。
