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

## tasks
任务进度回调通常通过 UI Dispatcher 异步排队，回调执行时任务可能已经取消或进入终态。调用 `ReportProgress`、`SetDescription`、`SetRunning`、`Complete` 等状态更新 API 前，必须再次检查任务仍处于活动状态（例如 `!task.IsTerminal && !task.IsCancellationRequested`）；检查后到调用之间仍需按竞态安全设计，不能对已取消或已完成的任务更新执行状态。取消操作也应优先请求父任务取消，让关联子任务通过链接的取消令牌一起停止。
涉及网络请求、文件下载、解压或其他长时间异步操作时，任务必须支持取消：使用任务提供的 `CancellationToken` 创建/调用 API，并将令牌继续传递到所有下游异步操作；收到取消后应尽快停止工作，不得启动新的后续步骤或吞掉取消信号。

## docs
- `docs/ai/ICONFONT.md` — iconfont 流程（AI）
- `docs/command-line.md`
- `docs/launch-placeholders.md`
