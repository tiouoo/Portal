<p align="center">
  <img src="assets/header.png" alt="Portal">
</p>

<p align="center">
  Portal - 开源、跨平台的 Minecraft 启动器与实例管理器
</p>

<p align="center">
  <a href="https://portal.tiouo.xyz/">官网</a> ·
  <a href="https://github.com/tiouoo/Portal/releases">下载</a> ·
  <a href="https://github.com/tiouoo/Portal/issues">反馈问题</a>
</p>

<p align="center">
  <a href="https://github.com/tiouoo/Portal/actions/workflows/publish-commit.yml"><img src="https://img.shields.io/github/actions/workflow/status/tiouoo/Portal/publish-commit.yml?branch=main&label=%E6%9E%84%E5%BB%BA&logo=github&style=flat-square" alt="构建状态"></a>
  <a href="https://github.com/tiouoo/Portal/releases"><img src="https://img.shields.io/github/v/release/tiouoo/Portal?display_name=tag&label=%E5%8F%91%E5%B8%83&logo=github&style=flat-square" alt="最新发布"></a>
  <a href="https://github.com/tiouoo/Portal/stargazers"><img src="https://img.shields.io/github/stars/tiouoo/Portal?label=Stars&logo=github&style=flat-square" alt="GitHub Stars"></a>
  <img src="https://img.shields.io/badge/License-GPL--3.0--or--later-6b4eff?style=flat-square" alt="GPL-3.0-or-later">
</p>

---

## 少一点配置，多一点游戏

Portal 是一个开源的 Minecraft 启动器。除了 Java 版，你也可以在这里安装、启动和整理 Windows 基岩版游戏，把不同版本、整合包和世界分开放好。

## 可以做什么

### 管理游戏

查看、搜索、排序、收藏和启动游戏。最近玩过的游戏和游玩时长也会显示在列表中，方便下次继续。

### 启动 Windows 基岩版

Portal 可以安装和启动 Windows 基岩版，也能将不同游戏版本、世界和资源分开管理。

### 查找和安装资源

直接浏览 Modrinth 和 CurseForge，安装模组、整合包、资源包、光影、数据包和地图。Portal 会把文件放到对应的游戏目录。

### 整理游戏文件

在一个地方查看游戏日志、存档、截图、设置和资源文件。需要找文件或看看游戏出了什么问题时，不必到处翻目录。

### 查看投影材料

打开 `.litematic` 文件，查看方块和容器中需要的材料，也可以导出结构。

## 支持内容

- 安装原版 Minecraft 和常用的 Java 版加载器，并直接启动游戏。
- 使用离线账户、微软账户或第三方账户登录。
- 管理 Java 版的模组、资源包、光影包、存档、截图和设置文件。
- 安装和启动 Windows 基岩版，管理游戏版本、世界、世界模板、行为包、资源包和皮肤包，并导入基岩版内容包。

> Windows 基岩版的安装、启动和资源管理依赖 Windows 平台接口，仅在 Windows 提供。Java 版可运行在 Windows、macOS 和 Linux。

## 下载 Portal

可从 [GitHub Releases](https://github.com/tiouoo/Portal/releases) 下载最新版本，直接选择：

- Windows 10 / 11 x64：[安装程序 `.exe`](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.win.x64.installer.exe) 或 [便携版 `.zip`](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.win.x64.portable.zip)
- macOS Apple Silicon：[dmg `.dmg`](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.osx.mac.arm64.dmg) 或 [应用包 `.app.zip`](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.osx.mac.arm64.app.zip)
- macOS Intel：[dmg `.dmg`](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.osx.mac.x64.dmg) 或 [应用包 `.app.zip`](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.osx.mac.x64.app.zip)
- Linux x64：[AppImage](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.linux.x64.AppImage)
- Linux ARM64：[AppImage](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.linux.arm64.AppImage)
- Linux ARM：[AppImage](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.linux.arm.AppImage)

> [!WARNING]
> macOS 首次打开 Portal 前，请先将 `Portal.app` 移动到“应用程序”文件夹，然后在终端运行以下命令：
>
> ```bash
> sudo xattr -rd com.apple.quarantine /Applications/Portal.app
> ```

## 从源代码运行

Portal 使用 GPL-3.0-or-later 许可证发布。

如果你想参与开发，需要安装 [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) 和 Git。运行以下命令获取项目：

```bash
git clone https://github.com/tiouoo/Portal.git
cd Portal
./update.bat
```

微软账户登录需要设置 `MICROSOFT_CLIENT_ID`；使用 CurseForge 时需要设置 `CURSEFORGE_API_KEY`。可在 [`.env.example`](.env.example) 查看变量名。

官网代码在 `/web` 目录：

```bash
cd web
npm i
npm run dev
```

## 致谢

Portal 建立在许多优秀的开源项目之上，包括但不限于 [Avalonia](https://avaloniaui.net/)、[MinecraftLaunch](https://github.com/Blessing-Studio/MinecraftLaunch)、[BedrockLauncher.Core](https://github.com/Round-Studio/BedrockLauncher.Core)、[LiteSkinViewer](https://github.com/Ktn429/LiteSkinViewer)
