# AGENTS.md

## iconfont
用户给 iconfont.cn zip 路径要更新/换图标字体 → 读并执行 `docs/ai/ICONFONT.md`。
图标一律用字体，禁止 StreamGeometry/`{StaticResource XxxGeometry}`。标识符 = iconfont `font_class`。
禁止为资源或图标自行创建映射列表、索引列表或字典封装。资源直接使用资源路径，字体图标直接使用 iconfont 的 Unicode，不要新增 `Glyphs`/`GetGlyph` 等映射。

## localization
所有用户可见文本必须使用本地化资源，不要在 XAML、C# 或其他 UI 代码中写死界面文案。新增文本要同步写入 `src/Portal.Localization/Localization/zh-CN` 和 `src/Portal.Localization/Localization/en-US` 对应资源，并通过 `Translate`、`CurrentValue()` 等现有本地化 API 使用。只有特定专有名词、产品名或中英文相同的技术标识（例如 `GravityCone`、`Java`、`NAT`、`SHA-256`）可以保留原文。

## build
```
dotnet build "E:\Portal\src\Portal.Desktop\Portal.Desktop.csproj" -c Debug
```
主 UI：`src/Portal`（Avalonia 12）。窗口基类在 TioUi 库（`module/Tio.Avalonia.Standard/`）。改子库后重建此命令验证。

## docs
- `docs/ai/ICONFONT.md` — iconfont 流程（AI）
- `docs/command-line.md`
- `docs/launch-placeholders.md`
