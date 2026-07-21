<p align="center">
  <img src="assets/Icon-Pattern.svg" width="112" alt="Portal">
</p>

<h1 align="center">Portal</h1>

<p align="center">
  <strong>你的 Minecraft，从这里出发。</strong><br>
  一个开源、跨平台的 Minecraft 启动器与实例管理器。
</p>

<p align="center">
  <a href="https://portal.tiouo.xyz/">官网</a> ·
  <a href="https://github.com/tiouoo/Portal/releases">下载</a> ·
  <a href="https://github.com/tiouoo/Portal/issues">反馈问题</a> ·
  <a href="#从源码运行">从源码运行</a>
</p>

<p align="center">
  <a href="https://github.com/tiouoo/Portal/actions/workflows/publish-commit.yml"><img src="https://img.shields.io/github/actions/workflow/status/tiouoo/Portal/publish-commit.yml?branch=main&label=%E6%9E%84%E5%BB%BA&logo=github&style=flat-square" alt="构建状态"></a>
  <a href="https://github.com/tiouoo/Portal/releases"><img src="https://img.shields.io/github/v/release/tiouoo/Portal?display_name=tag&label=%E5%8F%91%E5%B8%83&logo=github&style=flat-square" alt="最新发布"></a>
  <a href="https://github.com/tiouoo/Portal/stargazers"><img src="https://img.shields.io/github/stars/tiouoo/Portal?label=Stars&logo=github&style=flat-square" alt="GitHub Stars"></a>
  <img src="https://img.shields.io/badge/License-GPL--3.0--or--later-6b4eff?style=flat-square" alt="GPL-3.0-or-later">
</p>

---

## 少一点配置，多一点游戏

Portal 不只是把 Minecraft 打开。它把实例、资源、存档和游戏记录收进一个清晰的工作区，让不同版本、不同整合包和不同世界各归其位。

无论你是在维护一排 Java 版实例，还是想把 Windows 基岩版的版本、资源包和世界分开管理，Portal 都提供了一个不必绕路的入口。

<table>
  <tr>
    <td width="50%" valign="top">
      <h3>Java 与基岩版，同一个入口</h3>
      <p>在一个实例库中查看、搜索、排序、收藏并启动游戏。Java 版与 Windows 基岩版实例不再散落在不同工具里。</p>
    </td>
    <td width="50%" valign="top">
      <h3>为长期游玩准备</h3>
      <p>最近游玩、游戏时长、实例文件、设置与日志都留在手边。换版本、回到旧世界或整理资源时，不需要重新找路。</p>
    </td>
  </tr>
</table>

## 把复杂留在幕后

<table>
  <tr>
    <td width="50%" valign="top">
      <h3>01 / 整齐的实例库</h3>
      <p>管理多个游戏目录与实例。通过搜索、排序、收藏和快速启动，快速找到这一次想进入的世界。</p>
      <p><sub>Java 版 · Windows 基岩版 · 最近游玩 · 时长统计</sub></p>
    </td>
    <td width="50%" valign="top">
      <h3>02 / 资源发现无需绕路</h3>
      <p>直接在 Portal 内浏览 Modrinth 与 CurseForge 的内容，并把模组、整合包、资源包、光影、数据包和地图安装到合适的位置。</p>
      <p><sub>Modrinth · CurseForge · 整合包安装 · 依赖处理</sub></p>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>03 / 不止是启动游戏</h3>
      <p>实时查看游戏日志，查看存档信息，管理截图、配置、世界和资源文件。游戏外那些零碎但重要的事，也能在这里完成。</p>
      <p><sub>日志筛选 · 存档信息 · 文件管理 · 截图整理</sub></p>
    </td>
    <td width="50%" valign="top">
      <h3>04 / 投影材料，心里有数</h3>
      <p>打开 `.litematic` 文件，按分类统计方块与容器材料；在正式开工前，先弄清楚需要准备什么。</p>
      <p><sub>Litematica 解析 · 材料统计 · 容器分析 · 结构导出</sub></p>
    </td>
  </tr>
</table>

## 功能

| 能力 | 说明 |
| --- | --- |
| 游戏启动与安装 | 安装原版 Minecraft、常见 Java 版加载器与运行时，并从实例页直接启动游戏。 |
| 账户管理 | 支持离线账户、微软账户和 Yggdrasil 第三方认证服务器。 |
| Java 版资源管理 | 集中管理模组、资源包、光影包、存档、截图和配置文件。 |
| Windows 基岩版管理 | 管理版本隔离、世界、世界模板、行为包、资源包和皮肤包，并支持导入基岩版内容包。 |

> Windows 基岩版的安装、启动和资源管理依赖 Windows 平台接口，仅在 Windows 提供。Java 版可运行在 Windows、macOS 和 Linux。

## 下载 Portal

Nightly 构建会持续更新。想第一时间体验新功能，可以从 [GitHub Releases](https://github.com/tiouoo/Portal/releases) 获取版本，或直接选择你的平台：

| 平台 | 推荐下载 | 其他格式 |
| --- | --- | --- |
| Windows 10 / 11 x64 | [安装程序 `.exe`](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.win.x64.installer.exe) | [便携版 `.zip`](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.win.x64.portable.zip) |
| macOS Apple Silicon | [DMG `.dmg`](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.osx.mac.arm64.dmg) | [应用包 `.app.zip`](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.osx.mac.arm64.app.zip) |
| macOS Intel | [DMG `.dmg`](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.osx.mac.x64.dmg) | [应用包 `.app.zip`](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.osx.mac.x64.app.zip) |
| Linux x64 | [AppImage](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.linux.x64.AppImage) | [ARM64](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.linux.arm64.AppImage) · [ARM](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.linux.arm.AppImage) |

Linux 首次运行 AppImage：

```bash
chmod +x Portal.linux.x64.AppImage
./Portal.linux.x64.AppImage
```

## 从源码运行

Portal 使用 GPL-3.0-or-later 许可证发布。

如果你希望调试或参与开发，需要 [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) 和 Git。仓库包含子模块，克隆时请一并拉取：

```bash
git clone https://github.com/tiouoo/Portal.git
cd Portal
./update.bat
```

微软账户登录需要设置 `MICROSOFT_CLIENT_ID`；使用 CurseForge 资源服务时需要设置 `CURSEFORGE_API_KEY`。变量名见 [`.env.example`](.env.example)。

网站位于 `web/`，使用 Vue 和 Vite：

```bash
cd web
npm i
npm run dev
```

## 致谢

Portal 建立在许多优秀的开源项目之上，包括但不限于 [Avalonia](https://avaloniaui.net/)、[MinecraftLaunch](https://github.com/Blessing-Studio/MinecraftLaunch)、[BedrockLauncher.Core](https://github.com/Round-Studio/BedrockLauncher.Core)、[LiteSkinViewer](https://github.com/Ktn429/LiteSkinViewer)

<p align="center"><sub>Made for the worlds you keep returning to.</sub></p>
