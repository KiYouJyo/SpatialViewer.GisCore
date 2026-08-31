# SpatialViewer.GisCore

[中文](README.md) · [日本語](README.ja.md) · [English](README.en.md)

SpatialViewer 的独立 GIS 读图内核仓库。这里维护 GIS 数据源读取适配、空间参考、矢量/栅格语义模型、空间查询、渲染抽象与回归测试；WinUI 3 产品界面保留在 `KiYouJyo/SpatialViewer`。

> 当前阶段：**Phase 2 / 0.2.0 矢量格式与 CRS 基线已完成**。当前 managed baseline 已覆盖 GeoJSON、Shapefile、GeoPackage 矢量读取，显式 EPSG/WKT 语义与 4326↔3857 坐标转换，以及 backend-neutral packed R-tree。下一阶段进入 GeoTIFF/COG 等栅格读图链路。

## 当前能力

- **GeoJSON**：完整常用 Geometry 家族、Feature/FeatureCollection、属性、bbox、Z、CRS 缺失安全行为与 RenderFrame 转换。
- **Shapefile**：SHP/SHX/DBF/PRJ/CPG；Point/MultiPoint/PolyLine/Polygon 的 2D/Z/M；DBF 编码、PRJ CRS、范围候选索引。
- **GeoPackage**：feature tables、geometry columns、属性、nullable geometry、GeoPackageBinary/WKB、Z/M、SRS_ID 与文件内 RTree 范围查询。
- **CRS / Transform**：WKT1/WKT2 EPSG authority 识别、未知 WKT 保留、显式轴顺序策略、EPSG:4326 ↔ EPSG:3857 转换。
- **空间索引**：Core 提供 immutable packed R-tree；读取接口保持 `IAsyncEnumerable<GisFeature>`，不要求整层完整物化。

## 设计原则

- **UI 无关**：数据读取、CRS、几何、栅格、索引与场景转换不得依赖 WinUI 3 页面或控件。
- **坐标语义优先**：数据源 CRS、轴顺序、单位与原始 X/Y/Z/M 信息不得在导入阶段静默丢失。
- **矢量/栅格分流**：两类数据共享文档与图层抽象，但拥有各自的读取、缓存和渲染路径。
- **读取器隔离**：SQLite、未来的 GDAL/OGR、PROJ、NetTopologySuite 等实现只能存在于适配/服务层，不向上层公开第三方类型。
- **大数据友好**：接口支持流式读取、范围查询、取消令牌、空间索引和按需加载；真实百万级 benchmark 在 Phase 6 单独验收。
- **独立版本**：GIS 内核与 SpatialViewer UI 分别版本化，通过明确依赖版本集成。

## 仓库边界

本仓库是 GIS 读图内核的源代码归属。`SpatialViewer` 负责窗口、标签页、工具栏、图层面板和用户交互，只通过稳定接口使用本仓库提供的能力。

## 路线图

见 [`docs/ROADMAP.md`](docs/ROADMAP.md)。当前兼容范围见 [`docs/COMPATIBILITY.md`](docs/COMPATIBILITY.md)。

## 许可证

MIT License。第三方依赖及许可信息见 `THIRD-PARTY-NOTICES.md`。
