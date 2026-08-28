# AGENTS.md

## iconfont
用户给 iconfont.cn zip 路径要更新/换图标字体 → 读并执行 `docs/ai/ICONFONT.md`。
图标一律用字体，禁止 StreamGeometry/`{StaticResource XxxGeometry}`。标识符 = iconfont `font_class`。
禁止为资源或图标自行创建映射列表、索引列表或字典封装。资源直接使用资源路径，字体图标直接使用 iconfont 的 Unicode，不要新增 `Glyphs`/`GetGlyph` 等映射。

## build
```
dotnet build "E:\Portal\src\Portal.Desktop\Portal.Desktop.csproj" -c Debug
```
主 UI：`src/Portal`（Avalonia 12）。窗口基类在 TioUi 库（`module/Tio.Avalonia.Standard/`）。改子库后重建此命令验证。

## docs
- `docs/ai/ICONFONT.md` — iconfont 流程（AI）
- `docs/command-line.md`
- `docs/launch-placeholders.md`
