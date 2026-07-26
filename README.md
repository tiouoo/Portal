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

Portal 是一款开源、跨平台的 Minecraft 启动器与实例管理器，同时支持 Java 版和 Windows 基岩版，提供从游戏安装、账户登录到资源查找与文件整理的一体化体验，并对不同版本、整合包和世界进行独立管理

## 主要功能

### 游戏管理

- 查看、搜索、排序、收藏和启动游戏
- 在列表中显示最近游玩记录与游玩时长
- 安装原版 Minecraft 及常用的 Java 版加载器，并直接启动游戏

### 账户登录

- 支持离线账户、微软账户和第三方账户登录

### 资源安装

- 直接浏览 Modrinth 和 CurseForge
- 安装模组、整合包、资源包、光影、数据包和地图，文件自动放入对应的游戏目录

### 文件整理

- 集中查看游戏日志、存档、截图、设置和资源文件
- 管理 Java 版的模组、资源包、光影包、存档、截图和设置文件

### 投影材料

- 打开 `.litematic` 文件，查看方块和容器中所需的材料
- 导出投影结构

### 基岩版支持

- 安装和启动 Windows 基岩版
- 管理游戏版本、世界、世界模板、行为包、资源包和皮肤包
- 导入基岩版内容包

### 命令行调用

- 支持通过命令行参数或浏览器 `portal://` 链接调用安装与启动功能
- 详细用法参见 [命令行与 portal:// 协议](docs/command-line.md)

## 系统要求

- Java 版：Windows、macOS 和 Linux
- Windows 基岩版：仅 Windows 10 / 11

> Windows 基岩版的安装、启动和资源管理依赖 Windows 平台接口，仅在 Windows 提供

## 下载 Portal

可从 [GitHub Releases](https://github.com/tiouoo/Portal/releases) 下载对应平台的最新版本：

- Windows 10 / 11 x64：[安装程序 .exe](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.win.x64.installer.exe) 或 [便携版 .zip](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.win.x64.portable.zip)
- macOS Apple Silicon：[磁盘映像 .dmg](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.osx.mac.arm64.dmg) 或 [应用包 .app.zip](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.osx.mac.arm64.app.zip)
- macOS Intel：[磁盘映像 .dmg](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.osx.mac.x64.dmg) 或 [应用包 .app.zip](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.osx.mac.x64.app.zip)
- Linux x64：[AppImage](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.linux.x64.AppImage)
- Linux Arm64：[AppImage](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.linux.arm64.AppImage)
- Linux Arm：[AppImage](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.linux.arm.AppImage)

> [!NOTE]
> macOS 首次打开 Portal 前，请先将 `Portal.app` 移动到“应用程序”文件夹，然后在终端运行以下命令：
>
> ```bash
> sudo xattr -rd com.apple.quarantine /Applications/Portal.app
> ```

## 从源代码运行

Portal 使用 GPL-3.0-or-later 许可证发布

参与开发需要安装 [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) 和 Git，运行以下命令获取项目：

```bash
git clone https://github.com/tiouoo/Portal.git
cd Portal
./update.bat
```

微软账户登录需要设置 `MICROSOFT_CLIENT_ID`；使用 CurseForge 时需要设置 `CURSEFORGE_API_KEY`

可在 [`.env.example`](.env.example) 查看变量名

官网源代码位于 `web` 目录：

```bash
cd web
npm i
npm run dev
```

## 致谢

Portal 建立在许多优秀的开源项目之上，包括但不限于 [Avalonia](https://avaloniaui.net/)、[MinecraftLaunch](https://github.com/Blessing-Studio/MinecraftLaunch)、[BedrockLauncher.Core](https://github.com/Round-Studio/BedrockLauncher.Core)、[LiteSkinViewer](https://github.com/Ktn429/LiteSkinViewer)
