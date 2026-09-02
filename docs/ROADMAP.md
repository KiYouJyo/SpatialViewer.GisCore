# GIS 读图内核编写计划

## 总目标

建立一个与 WinUI 3 界面彻底解耦、可独立版本化的 GIS 读图内核。优先保证**正确读取、坐标不丢失、常见格式覆盖、大文件可用、渲染接口稳定**，再扩展编辑/分析能力。

## Phase 0 — 仓库与契约基线（0.1.0-alpha）

- 对齐 CadCore 的仓库文本、CI、测试、版本与目录规范。
- 建立 `Core / Formats / Rendering / Windows backend / Tests` 分层。
- 定义坐标、Extent、CRS、Feature、VectorLayer、RasterLayer、Dataset 元数据。
- 定义格式探测、异步打开、取消、范围读取、错误分类接口。
- 建立首批契约测试，确保上层 UI 不接触第三方 GIS 类型。

**验收条件**：solution 可 restore/build/test；公开模型无 WinUI/GDAL/PROJ 类型泄漏。

## Phase 1 — 首条端到端矢量链路（0.1.0）

**状态：✅ 已完成（2026-08-31）**

- [x] 完整 GeoJSON：Point/MultiPoint/LineString/MultiLineString/Polygon/MultiPolygon/GeometryCollection。
- [x] 属性类型、空值、Feature ID、bbox、Z、CRS 缺失行为。
- [x] 支持 FeatureCollection、单 Feature 与直接 Geometry 顶层对象。
- [x] 矢量图层范围计算、基本空间过滤、RenderFrame 转换。
- [x] Multi* / GeometryCollection 转换为稳定的 Point / Polyline / Polygon 渲染 primitive。
- [x] 建立小型/畸形 fixture 与运行时生成的 4096-feature 压力回归。
- [x] 缺失 CRS 保持 `Unknown`；仅保留文件显式声明的 legacy named CRS，不隐式假定 EPSG:4326。
- [x] 未闭合 Polygon 与额外坐标维度直接诊断，不静默修正或丢弃。

**验收结果**：GeoJSON 从 Reader → GIS semantic model → extent query → RenderFrame 的端到端链路已闭环；Debug/Release build 与 Release tests 通过严格 analyzer/CI 验证。

## Phase 2 — 桌面 GIS 主流矢量格式（0.2.x）

**状态：✅ 已完成（2026-08-31）**

- [x] Shapefile：SHP/SHX/DBF/PRJ/CPG 组合读取，DBF 编码处理，缺失 PRJ 保持 `Unknown`。
- [x] Shapefile Point/MultiPoint/PolyLine/Polygon 的 2D/Z/M 语义读取；Z/M 不静默丢失。
- [x] GeoPackage 矢量表、几何列、属性、nullable geometry、GeoPackageBinary/WKB、SRS_ID 与文件内 RTree 范围查询。
- [x] CRS 基线：WKT1/WKT2 中 EPSG authority 识别，未知 WKT 原样保留；显式 EPSG:4326 ↔ EPSG:3857 坐标转换。
- [x] 轴顺序作为显式 transform policy；坐标转换保留 Z/M，越界和不支持的 EPSG 对明确报错。
- [x] Core 提供 backend-neutral immutable packed R-tree；Shapefile extent query 先读取轻量记录 bbox 建候选集，再完整解析候选 geometry/DBF。
- [x] Reader 继续使用 `IAsyncEnumerable<GisFeature>` 流式枚举，不要求把完整图层强制物化为 Feature 列表。
- [x] Projections / Shapefile / GeoPackage 已正式纳入 solution 的 Debug/Release 构建配置。
- [x] 当前保持 managed reference implementation；第三方实现必须继续隔离在 adapter/service 层。

**验收结果**：Shapefile 与 GeoPackage 均已有合法合成数据回归，坐标/属性/Z/M/CRS/extent query 可验证；Shapefile 空间索引仅保存 bbox 与记录引用，GeoPackage 优先复用文件内 RTree，因此范围读取不需要先构造全部 `GisFeature`。百万级真实数据集的时间、峰值内存和缓存 benchmark 仍属于 Phase 6 的性能收敛任务，不在此处虚报测试结果。

**后续扩展**：GDAL/OGR + PROJ 可作为更广格式/CRS 覆盖的可选 backend 引入，但不得替换或污染当前稳定公共契约。

## Phase 3 — 栅格读图（0.3.x）

**状态：✅ 已完成 managed baseline（2026-08-31）**

- [x] Core 栅格契约：affine `RasterGeoTransform`、pixel window、band/overview metadata、RGBA read result、pixel anchor。
- [x] GeoTIFF：ModelPixelScale/ModelTiepoint 与 ModelTransformation、GeoKey EPSG、PixelIsArea/PixelIsPoint、nodata、颜色模型、band metadata。
- [x] tiled TIFF 仅解码与请求窗口相交的 tile；strip TIFF 仅解码与窗口相交的 strip，不先构造整幅 RGBA 图。
- [x] 内部 TIFF overview 识别与按输出分辨率选择；窗口映射到 overview 后再解码/重采样。
- [x] 本地 COG-compatible tiled/overview 读取路径建立；HTTP Range/远程 COG 在 Phase 4 补齐。
- [x] PNG/JPEG + world file（PGW/JGW/长扩展名/WLD）与可选同名 PRJ；world-file 像素中心语义正规化为 Core 像素外框 affine transform。
- [x] byte-budgeted LRU raster cache、`RasterViewportReader` 与 latest-request cancellation；新视口读取会取消被替代的旧请求。
- [x] GeoTIFF、WorldImage 项目正式纳入 solution Debug/Release 配置。
- [x] 合法合成 tiled/overview GeoTIFF、strip GeoTIFF、PNG、JPEG fixture 与地理定位/像素方向/窗口读取回归。

**验收结果**：GeoTIFF 缩放/平移读取走 tile/strip 窗口路径并可利用内部 overview，不要求先解完整原图；地理定位与旋转 affine/world-file 测试通过。PNG/JPEG 由于格式本身不是随机 tile 容器，首次像素请求仍需完整解压缩图像，随后可通过弱引用解码缓存与 raster viewport cache 复用；该限制在兼容矩阵中明确记录。

## Phase 4 — 瓦片与网络数据源（0.4.x）

**状态：✅ 已完成 managed baseline（2026-09-02）**

- [x] Core 建立 backend-neutral tile contract：canonical XYZ 坐标、TMS 边界转换、Web Mercator tile bounds、encoded payload、byte-budgeted LRU cache 与 latest-request cancellation。
- [x] MBTiles：SQLite metadata/tiles 表、TMS `tile_row` → canonical XYZ 转换、PNG/JPEG/WebP/MVT encoded payload；连接使用 read-only/private-cache/no-pooling。
- [x] XYZ/TMS HTTP template：URL 模板、Y 翻转、404→null、超时、调用方取消、429/5xx/网络异常重试、ETag/Last-Modified 与独立 tile cache。
- [x] Remote COG：HTTP Range-backed LibTiff stream，必须返回 `206 Partial Content` 与合法 `Content-Range`；支持 tiled GeoTIFF window read、internal overview 与 bounded range cache，不回退为整文件下载。
- [x] WMS 1.3.0：独立 map-image request/result contract、GetMap、BBOX/尺寸/格式参数；EPSG:4326 明确遵循纬度优先轴序。
- [x] WMTS 1.0.0：KVP GetTile baseline、TileMatrix 标识模板、XYZ/TMS row 边界转换与 tile cache。
- [x] Managed MVT：自有 protobuf 读取、keys/values/tags、delta+zigzag geometry commands，Point/MultiPoint/LineString/MultiLineString/Polygon/MultiPolygon 转换为现有 `GisFeature` / `IGisGeometry`，tile-local 坐标映射至 EPSG:3857。
- [x] PMTiles v3：127-byte header、Hilbert TileID、root/leaf varint directory、本地随机读取与远程 HTTP Range；支持 None/GZip/Brotli，MVT/PNG/JPEG/WebP；Zstd、AVIF、MapLibre Vector Tile 保持显式未支持边界。
- [x] Phase 4 的 MBTiles / XyzTiles / RemoteCog / WebMap / Mvt / PmTiles 均为 solution 的直接 Debug/Release 成员，不依赖测试项目间接构建。
- [x] 所有网络协议测试均使用确定性的自定义 `HttpMessageHandler` / synthetic archive，不依赖公共互联网。

**验收结果**：本阶段的本地/网络瓦片、远程随机访问栅格、WMS/WMTS 与 MVT/PMTiles 读取链已闭环；远程 COG 与 PMTiles 都有“服务器忽略 Range 返回 200 时必须拒绝”的回归，避免隐藏整文件下载。严格 analyzer 下 Debug/Release 0 warning / 0 error；当前功能基线为 Core 16 + Formats 84 + Rendering 3 = 103/103 tests。最终发布 head 仍需在版本/文档收口后再次通过相同 CI，作为 0.4.0 权威验收记录。

## Phase 5 — GIS 显示语义（0.5.x）

- 分级/分类/唯一值样式模型。
- 线型、填充、符号、透明度、最小/最大显示比例尺。
- 标签布局基础：字段表达式、碰撞、优先级、视口裁剪。
- 评估 SLD/QML 等样式导入，但不让格式细节污染核心样式模型。

## Phase 6 — 性能与兼容性收敛（0.6–0.9）

- 空间索引、对象池、几何简化、LOD、tile/feature cache。
- 百万级/更大数据集压力测试、真实内存上限测试与 benchmark。
- 大型 GeoTIFF/COG 的实际峰值内存、tile cache 命中率与快速拖拽取消压力测试。
- 网络 tile/PMTiles/remote COG 的连接复用、Range cache 命中率与快速拖拽压力测试。
- 与 QGIS/GDAL 的已知样例对照，维护兼容矩阵。
- 崩溃/损坏文件 fuzz fixtures；所有 reader 错误必须可隔离。

## 1.0 验收线

- P0：GeoJSON、Shapefile、GeoPackage、GeoTIFF + CRS 转换稳定。
- 常用二维要素/栅格/瓦片源在 SpatialViewer 中显示结果可与主流 GIS 软件进行基准对照。
- 大文件采用按需读取，不因单一图层导致 UI 进程不可恢复性内存峰值；远程随机访问源不得偷偷退化为整文件下载。
- 公共 API 有版本策略；第三方 native 依赖的许可、打包和更新边界明确。
- 每个已宣称支持的格式均有合法测试 fixture 与回归测试。

## 明确不在首版做的事情

编辑、空间分析、地理处理模型、拓扑修复、制图排版不是 GisCore 1.0 的前置条件。读图内核先把“读对、放对、画对、不卡死”做好。
