# 贡献指南

1. 从 `main` 创建功能分支；保持一次 PR 聚焦一个明确问题。
2. 运行 `dotnet build SpatialViewer.GisCore.sln -c Release` 与 `dotnet test SpatialViewer.GisCore.sln -c Release`。
3. 新增/修复格式、CRS、几何或渲染行为时必须补测试；可公开再分发的样例放在 `tests/fixtures`。
4. 第三方 GIS 库必须放在 adapter 项目，禁止把其公开类型泄漏到 Core/Rendering API。
5. Native 依赖、数据集许可或再分发条件有变化时同步更新 `THIRD-PARTY-NOTICES.md`。
