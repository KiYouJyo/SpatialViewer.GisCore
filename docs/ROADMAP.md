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

- 完整 GeoJSON：Point/MultiPoint/LineString/MultiLineString/Polygon/MultiPolygon/GeometryCollection。
- 属性类型、空值、Feature ID、bbox、CRS 缺失行为。
- 矢量图层范围计算、基本空间过滤、RenderFrame 转换。
- 建立小型/畸形/大文件 fixture 与回归测试。

**验收条件**：GeoJSON 从读取到显示链路闭环；错误文件可诊断；不静默修正坐标。

## Phase 2 — 桌面 GIS 主流矢量格式（0.2.x）

- Shapefile（SHP/SHX/DBF/PRJ/CPG）与编码处理。
- GeoPackage 矢量表、几何列、属性、空间索引。
- CRS 服务：EPSG/WKT 解析、显式坐标转换、轴顺序策略。
- R-tree/分块索引、extent query、流式 feature enumeration。
- 第三方后端采用独立 adapter；优先评估 GDAL/OGR + PROJ，同时保留 managed reference adapter。

**验收条件**：百万级要素不会被强制一次性物化；Shapefile/GPKG 坐标与属性可回归验证。

## Phase 3 — 栅格读图（0.3.x）

- GeoTIFF：GeoTransform、CRS、nodata、颜色模型、overview。
- COG/tiled TIFF 的窗口读取与 mip/overview 选择。
- PNG/JPEG + world file。
- Raster tile cache、视口范围请求、取消过期任务。

**验收条件**：大栅格缩放/平移不整图解码；地理定位与像素范围测试通过。

## Phase 4 — 瓦片与网络数据源（0.4.x）

- MBTiles、XYZ/TMS。
- WMS/WMTS；网络请求、缓存、超时、重试与取消全部在 adapter 层。
- MVT，随后评估 PMTiles。
- 网络源与本地源共享统一图层接口，但缓存策略分离。

## Phase 5 — GIS 显示语义（0.5.x）

- 分级/分类/唯一值样式模型。
- 线型、填充、符号、透明度、最小/最大显示比例尺。
- 标签布局基础：字段表达式、碰撞、优先级、视口裁剪。
- 评估 SLD/QML 等样式导入，但不让格式细节污染核心样式模型。

## Phase 6 — 性能与兼容性收敛（0.6–0.9）

- 空间索引、对象池、几何简化、LOD、tile/feature cache。
- 大数据压力测试与内存上限测试。
- 与 QGIS/GDAL 的已知样例对照，维护兼容矩阵。
- 崩溃/损坏文件 fuzz fixtures；所有 reader 错误必须可隔离。

## 1.0 验收线

- P0：GeoJSON、Shapefile、GeoPackage、GeoTIFF + CRS 转换稳定。
- 常用二维要素/栅格在 SpatialViewer 中显示结果可与主流 GIS 软件进行基准对照。
- 大文件采用按需读取，不因单一图层导致 UI 进程不可恢复性内存峰值。
- 公共 API 有版本策略；第三方 native 依赖的许可、打包和更新边界明确。
- 每个已宣称支持的格式均有合法测试 fixture 与回归测试。

## 明确不在首版做的事情

编辑、空间分析、地理处理模型、拓扑修复、制图排版不是 GisCore 1.0 的前置条件。读图内核先把“读对、放对、画对、不卡死”做好。
