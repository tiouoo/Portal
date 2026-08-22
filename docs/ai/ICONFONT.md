# ICONFONT (AI)

## 触发条件
用户给出 iconfont.cn 下载 zip 路径，要求"更新/替换/换图标字体"。或要求增删图标字体。

## 更新字体流程
```powershell
Expand-Archive -LiteralPath "<zip>" -DestinationPath "<tmp>" -Force
Copy-Item "<tmp>\font_*\iconfont.json" "E:\Portal\docs\ai\iconfont.json" -Force
Copy-Item "<tmp>\font_*\iconfont.ttf" "E:\Portal\src\Portal\Assets\Fonts\iconfont.ttf" -Force
```
编辑 `src\Portal\Module\IconResources.cs` 的 `Glyphs` 字典：
- JSON `glyphs[]` 每项：`font_class`=标识符，`unicode`=十六进制码
- C# 写法：`{ "font_class", "\u" + unicode }`（如 `"trash-can" -> "\ue640"`）
- 新增/变动的条目加入；标识符变了就同步改引用

引用位置：
- XAML `{icons:IconGlyph 旧名}` → 新名
- C# `IconResources.GetGlyph("旧名")` / `CreateIcon("旧名", ...)`
- 窗口按钮：`src\Portal\Styles\Style.axaml` 的 `tio|TioTitleBar` Setter `Value="&#xXXXX;"`

重建验证：
```powershell
dotnet build "E:\Portal\src\Portal.Desktop\Portal.Desktop.csproj" -c Debug
```

## 渲染图标
XAML：
```xml
xmlns:icons="using:Portal.Module"
<TextBlock FontFamily="{StaticResource IconFont}" Text="{icons:IconGlyph 名字}" FontSize="16"/>
```
C#：`IconResources.CreateIcon("名字", size)` 或 `IconResources.GetGlyph("名字")` / `IconResources.IconFont`
绑定：`Text="{Binding X, Converter={x:Static icons:IconGlyphConverter.Instance}}"`
标签页图标（PageInfo）：设 `IconGlyph = IconResources.GetGlyph("名字"); IconFont = IconResources.FontFamilyName;`

## 规则
- 标识符 = iconfont `font_class`（短横线小写）
- 禁止 StreamGeometry / `{StaticResource XxxGeometry}`（已移除）
- 新图标：等用户上传到 iconfont.cn 并导出新 zip 后，按"更新字体流程"更新即可

## TTF 更换
- 嵌入资源（`avares://Portal/Assets/Fonts/iconfont.ttf`）：替换文件 + 重建即可
- 运行时外部加载：`FontManager.Current.AddFontCollection(new EmbeddedFontCollection(new Uri("fonts:appicons", UriKind.Absolute), new Uri("file:///path/iconfont.ttf", UriKind.Absolute)))`，字体名 `fonts:appicons#iconfont`
