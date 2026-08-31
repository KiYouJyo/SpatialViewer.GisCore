# SpatialViewer.GisCore

[中文](README.md) · [日本語](README.ja.md) · [English](README.en.md)

SpatialViewer 的独立 GIS 读图内核仓库。这里维护 GIS 数据源读取适配、空间参考、矢量/栅格语义模型、空间查询、渲染抽象与回归测试；WinUI 3 产品界面保留在 `KiYouJyo/SpatialViewer`。

> 当前阶段：建立与 `SpatialViewer.CadCore` 一致的独立构建/测试边界，先完成 GIS 基础模型、格式适配接口与可回归骨架，再逐步接入 GeoJSON、Shapefile、GeoPackage、GeoTIFF 等常用格式。

## 设计原则

- **UI 无关**：数据读取、CRS、几何、栅格、索引与场景转换不得依赖 WinUI 3 页面或控件。
- **坐标语义优先**：数据源 CRS、轴顺序、单位与原始坐标信息不得在导入阶段静默丢失。
- **矢量/栅格分流**：两类数据共享文档与图层抽象，但拥有各自的读取、缓存和渲染路径。
- **读取器隔离**：GDAL/OGR、PROJ、NetTopologySuite 等第三方实现只能存在于适配层，不向上层公开第三方类型。
- **大数据友好**：接口从一开始支持流式读取、范围查询、取消令牌、空间索引和按需加载。
- **独立版本**：GIS 内核与 SpatialViewer UI 分别版本化，通过明确依赖版本集成。

## 仓库边界

本仓库是 GIS 读图内核的源代码归属。`SpatialViewer` 负责窗口、标签页、工具栏、图层面板和用户交互，只通过稳定接口使用本仓库提供的能力。

## 路线图

见 [`docs/ROADMAP.md`](docs/ROADMAP.md)。

## 许可证

MIT License。第三方依赖及许可信息见 `THIRD-PARTY-NOTICES.md`。
