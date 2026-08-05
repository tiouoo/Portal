<p align="center">
  <a href="https://ifdian.net/a/tiouo">
    <img src="assets/top.png" alt="Portal Top">
  </a>
  <a href="https://portal.tiouo.cc">
    <img src="assets/header.png" alt="Portal">
  </a>
</p>

<p align="start">
  <a href="https://github.com/tiouoo/Portal/actions/workflows/publish-commit.yml"><img src="https://img.shields.io/github/actions/workflow/status/tiouoo/Portal/publish-commit.yml?branch=main&label=%E6%9E%84%E5%BB%BA&logo=github&style=flat-square" alt="构建状态"></a>
  <a href="https://github.com/tiouoo/Portal/releases"><img src="https://img.shields.io/github/v/release/tiouoo/Portal?display_name=tag&label=%E5%8F%91%E5%B8%83&logo=github&logoColor=white&color=ff007f&style=flat-square" alt="最新发布"></a>
  <a href="https://github.com/tiouoo/Portal/releases"><img src="https://img.shields.io/github/v/release/tiouoo/Portal?include_prereleases&display_name=tag&label=%E9%A2%84%E5%8F%91%E5%B8%83&logo=github&logoColor=white&color=9c27b0&style=flat-square" alt="预发布"></a>  
  <a href="https://github.com/tiouoo/Portal/stargazers"><img src="https://img.shields.io/github/stars/tiouoo/Portal?label=Stars&logo=github&logoColor=white&color=eac54f&style=flat-square" alt="GitHub Stars"></a>
  <img src="https://img.shields.io/badge/License-GPL--3.0--or--later-9d4edd?logoColor=white&style=flat-square" alt="GPL-3.0-or-later">
  <a href="https://portal.tiouo.cc"><img src="https://img.shields.io/static/v1?label=%E5%AE%98%E7%BD%91&message=portal.tiouo.cc&color=38ce8f&logo=globe&logoColor=white&style=flat-square" alt="官网"></a>
  <a href="https://ifdian.net/a/tiouo"><img src="https://img.shields.io/static/v1?label=%E7%88%B1%E5%8F%91%E7%94%B5&message=ifdian.net/a/tiouo&color=f89aba&logo=afdian&logoColor=white&style=flat-square" alt="爱发电"></a>
</p>

---

## 少一点配置，多一点游戏

<a href="https://portal.tiouo.cc">Portal</a> 是一款开源、跨平台的 Minecraft 启动器与实例管理器，同时支持 Java 版和基岩版，提供从游戏安装、账户登录到资源查找与文件整理的一体化体验，并对不同版本、整合包和世界进行独立管理

## 下载 Portal

从 [Releases](https://github.com/tiouoo/Portal/releases) 下载对应平台的最新版本：

| 平台                    | commit 版本                                                                                                                                                                                                                                                                                                 | nightly 版本                                                                                                                                                                                                                                                                                                   |
| ----------------------- |-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Windows 10 / 11 x64** | [安装程序](https://github.com/tiouoo/Portal/releases/download/publish-commit/Portal.win.x64.installer.zip) / [便携版](https://github.com/tiouoo/Portal/releases/download/publish-commit/Portal.win.x64.portable.zip)                                                                                        | [安装程序](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.win.x64.installer.zip) / [便携版](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.win.x64.portable.zip)                                                                                         |
| **macOS Apple Silicon** | [磁盘映像](https://github.com/tiouoo/Portal/releases/download/publish-commit/Portal.osx.mac.arm64.dmg) / [应用包](https://github.com/tiouoo/Portal/releases/download/publish-commit/Portal.osx.mac.arm64.app.zip)                                                                                           | [磁盘映像](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.osx.mac.arm64.dmg) / [应用包](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.osx.mac.arm64.app.zip)                                                                                            |
| **macOS Intel**         | [磁盘映像](https://github.com/tiouoo/Portal/releases/download/publish-commit/Portal.osx.mac.x64.dmg) / [应用包](https://github.com/tiouoo/Portal/releases/download/publish-commit/Portal.osx.mac.x64.app.zip)                                                                                               | [磁盘映像](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.osx.mac.x64.dmg) / [应用包](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.osx.mac.x64.app.zip)                                                                                                |
| **Linux x64**           | [AppImage](https://github.com/tiouoo/Portal/releases/download/publish-commit/Portal.linux.x64.AppImage) / [deb包](https://github.com/tiouoo/Portal/releases/download/publish-commit/Portal.linux.x64.deb) / [rpm包](https://github.com/tiouoo/Portal/releases/download/publish-commit/Portal.linux.x64.rpm) | [AppImage](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.linux.x64.AppImage) / [deb包](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.linux.x64.deb) / [rpm包](https://github.com/tiouoo/Portal/releases/download/publish-nightly/Portal.linux.x64.rpm) |

> [!NOTE]
> macOS 首次打开 Portal 前，请先将 `Portal.app` 移动到“应用程序”文件夹，然后在终端运行以下命令：
>
> ```bash
> sudo xattr -rd com.apple.quarantine /Applications/Portal.app
> ```

## 功能

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

### 投影材料 · 开发中

- 打开 `.litematic` `.nbt` 文件，预览结构、查看所需的材料
- 导出材料列表

### 基岩版支持

- Windows x64 支持 GDK、UWP 游戏本体的下载、安装与启动，以及 DLL 模组、预加载和可配置鼠标锁
- Linux x64 支持 GDK 游戏本体的下载、安装与 Proton 启动；首次使用会自动下载 GDK-Proton，UWP 和 DLL 注入不适用于 Linux
- 管理游戏版本、世界、世界模板、行为包、资源包和皮肤包
- 导入基岩版内容包

### 命令行调用

- 支持通过命令行参数或浏览器 `portal://` 链接调用安装与启动功能
- 详细用法参见 [命令行与 portal:// 协议](docs/command-line.md)

## 从源代码运行

```bash
git clone https://github.com/tiouoo/Portal.git
cd Portal
./update.bat
```

环境变量 [`[root]/.env.example`](.env.example) :

```env
CURSEFORGE_API_KEY=
MICROSOFT_CLIENT_ID=
```

官网 [`[root]/web`](web) :

```bash
cd web
npm i
npm run dev
```

## 致谢

Portal 建立在许多优秀的 [开源项目](src/Directory.Packages.props) 之上

部分基岩版组件来自 [Round-Studio/PreLoadCpp](https://github.com/Round-Studio/PreLoadCpp) 与 [Round-Studio/Uwp.Injector](https://github.com/Round-Studio/Uwp.Injector)

> [!NOTE]
> 项目中使用的部分开源库进行了二次修改：
> - **MinecraftLaunch**：原项目 [Blessing-Studio/MinecraftLaunch](https://github.com/Blessing-Studio/MinecraftLaunch) / 二改 [tiouoo/MinecraftLaunch](https://github.com/tiouoo/MinecraftLaunch)
> - **LiteSkinViewer**：原项目 [Ktn429/LiteSkinViewer](https://github.com/Ktn429/LiteSkinViewer) / 二改 [tiouoo/LiteSkinViewer](https://github.com/tiouoo/LiteSkinViewer)

在交互设计、功能取舍与跨平台体验的探索中，Portal 也从下列开源项目的实践中获得了许多启发。感谢所有维护者与贡献者持续丰富 Minecraft 启动器生态；Portal 以独立的产品定位、架构与实现持续开发。

- [BedrockBoot](https://github.com/Round-Studio/BedrockBoot)
- [LauncherX](https://github.com/Corona-Studio/LXIT)
- [Axolotl](https://github.com/Mystic-Stars/Axolotl)
- [PCL-CE](https://github.com/PCL-Community/PCL-CE)
- [Polymerium](https://github.com/d3ara1n/Polymerium)
- [HMCL](https://github.com/HMCL-dev/HMCL)
- [BakaXL](https://bakaxl.com)
