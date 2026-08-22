# AGENTS.md

## iconfont
用户给 iconfont.cn zip 路径要更新/换图标字体 → 读并执行 `docs/ai/ICONFONT.md`。
图标一律用字体，禁止 StreamGeometry/`{StaticResource XxxGeometry}`。标识符 = iconfont `font_class`。

## build
```
dotnet build "E:\Portal\src\Portal.Desktop\Portal.Desktop.csproj" -c Debug
```
主 UI：`src/Portal`（Avalonia 12）。窗口基类在 TioUi 库（`module/Tio.Avalonia.Standard/`）。改子库后重建此命令验证。

## docs
- `docs/ai/ICONFONT.md` — iconfont 流程（AI）
- `docs/command-line.md`
- `docs/launch-placeholders.md`
