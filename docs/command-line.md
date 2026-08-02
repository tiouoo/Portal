# 命令行与 portal:// 协议

Portal 支持通过命令行参数或 `portal://` 链接调用启动器内的安装与启动功能。两种入口的参数一一对应，解析后执行的是同一套逻辑。

如果 Portal 已在运行，命令会转发给正在运行的实例执行；否则会先启动 Portal，界面加载完成后再执行。安装进度显示在任务抽屉中，完成或失败会弹出通知。

## 命令行

可执行文件为 `Portal.Desktop.exe`（Linux / macOS 为对应平台的可执行文件）。

```
Portal.Desktop.exe install vanilla <版本> [--folder <文件夹>] [--id <实例ID>]
Portal.Desktop.exe install loader <版本> --loader <加载器[@版本]> [--loader ...] [--folder <文件夹>] [--id <实例ID>]
Portal.Desktop.exe install modpack <来源> [--from modrinth|curseforge] [--version <版本或fileId>] [--folder <文件夹>] [--id <实例ID>]
Portal.Desktop.exe launch <实例ID> [--folder <文件夹>] [--world <世界文件夹>]
Portal.Desktop.exe launch <实例ID> [--folder <文件夹>] [--server <服务器地址>] [--port <端口>]
Portal.Desktop.exe help
```

`install` 也可写作 `download`，两者等价。

### 示例

```powershell
# 安装原版
Portal.Desktop.exe install vanilla 1.21.8

# 安装原版并附带最新版 Fabric
Portal.Desktop.exe install loader 1.21.8 --loader fabric

# 安装指定版本的 Forge，并自定义实例 ID 和目标文件夹
Portal.Desktop.exe install loader 1.20.1 --loader forge@47.2.0 --folder "D:\Minecraft\.minecraft" --id "1.20.1-forge"

# 从本地文件安装整合包
Portal.Desktop.exe install modpack "D:\packs\pack.mrpack"

# 从直链安装整合包
Portal.Desktop.exe install modpack "https://cdn.modrinth.com/data/1KVo5zza/versions/cZY3Bvs9/Fabulously.Optimized-v14.0.0-beta.2.mrpack"

# 按名称搜索并安装最新版本（自动在 Modrinth / CurseForge 查找）
Portal.Desktop.exe install modpack "Fabulously Optimized"

# 按项目 ID 安装，并指定版本
Portal.Desktop.exe install modpack fabulously-optimized --from modrinth --version 14.0.0-beta.2
Portal.Desktop.exe install modpack 715572 --from curseforge --file 6985843

# 启动实例
Portal.Desktop.exe launch "1.20.1-forge"
Portal.Desktop.exe launch "1.20.1-forge" --folder "D:\Minecraft\.minecraft"

# 启动并直接进入某个世界（传世界在 saves 目录下的文件夹名；版本隔离下同名世界可重复，以文件夹名区分）
Portal.Desktop.exe launch "1.20.1-forge" --folder "D:\Minecraft\.minecraft" --world "New World"

# 启动并直接进入服务器（--port 缺省为 25565）
Portal.Desktop.exe launch "1.20.1-forge" --folder "D:\Minecraft\.minecraft" --server "play.example.com" --port 25565
```

## 浏览器命令

在设置 → 默认行为 → Portal 协议中注册协议后，浏览器地址栏或网页链接可以直接调起启动器。macOS 版无需注册，协议已在应用包中声明；Linux 通过包管理器或 AppImage 桌面集成安装时也会自动注册。

与上面命令行等价的链接：

```
portal://install/vanilla?version=1.21.8
portal://install/loader?version=1.21.8&loader=fabric
portal://install/loader?version=1.20.1&loader=forge@47.2.0&folder=D%3A%5CMinecraft%5C.minecraft&id=1.20.1-forge
portal://install/modpack?source=https%3A%2F%2Fcdn.modrinth.com%2Fdata%2F1KVo5zza%2F...
portal://install/modpack?source=Fabulously%20Optimized
portal://install/modpack?source=fabulously-optimized&from=modrinth&version=14.0.0-beta.2
portal://install/modpack?source=715572&from=curseforge&file=6985843
portal://launch?id=1.20.1-forge&folder=D%3A%5CMinecraft%5C.minecraft
portal://launch?id=1.20.1-forge&folder=D%3A%5CMinecraft%5C.minecraft&world=New%20World
portal://launch?id=1.20.1-forge&folder=D%3A%5CMinecraft%5C.minecraft&server=play.example.com&port=25565
```

`portal://launch/<实例ID>` 这种把实例 ID 放在路径里的写法也被接受。

## 格式说明

### 结构

URI 的路径部分对应命令行的动词和子命令，查询参数对应命令行选项：

```
portal://<动词>/<子命令>?<参数>=<值>&<参数>=<值>
```

| 命令行 | URI |
| --- | --- |
| `install vanilla 1.21.8` | `portal://install/vanilla?version=1.21.8` |
| `install loader 1.21.8 --loader fabric` | `portal://install/loader?version=1.21.8&loader=fabric` |
| `install modpack <来源>` | `portal://install/modpack?source=<来源>` |
| `launch <实例ID>` | `portal://launch?id=<实例ID>` |
| `launch <实例ID> --world <世界文件夹>` | `portal://launch?id=<实例ID>&world=<世界文件夹>` |
| `launch <实例ID> --server <地址> [--port <端口>]` | `portal://launch?id=<实例ID>&server=<地址>&port=<端口>` |

### 参数

| 命令行 | URI 参数 | 说明 |
| --- | --- | --- |
| 位置参数（版本） | `version` / `v` | Minecraft 版本号 |
| 位置参数（来源） | `source` / `url` / `path` / `project` | 整合包来源，见下 |
| 位置参数（实例 ID） | `id` | launch 要启动的实例 |
| `--loader` / `-l` | `loader`（可重复或逗号分隔） | 加载器，可用 `@` 指定版本，如 `fabric@0.16.9` |
| `--folder` / `-f` / `--dir` | `folder` / `dir` | 目标 Minecraft 文件夹 |
| `--id` | `id` | 安装时的自定义实例 ID |
| `--from` / `--platform` | `from` / `platform` | 整合包平台：`modrinth` 或 `curseforge` |
| `--version` / `-v` / `--file` | `version` / `v` / `file` | 整合包版本，见下 |
| `--world` | `world` | 直接进入的世界（saves 目录下的文件夹名），需配合 `launch` |
| `--server` / `--address` | `server` / `address` | 直接进入的服务器地址，需配合 `launch` |
| `--port` | `port` | 服务器端口（1-65535），缺省 25565，需配合 `--server` |

### 取值规则

加载器：`fabric` / `forge` / `neoforge` / `quilt` / `optifine`。主加载器只能选一个，OptiFine 可以单独安装或与 Forge 组合。不带 `@版本` 时安装最新版。Forge、NeoForge、OptiFine 需要在设置中配置有效的 Java。

整合包来源按以下顺序识别：

1. `http://` 或 `https://` 开头视为直链，下载后安装；
2. 存在的本地文件路径，直接安装；
3. 其余视为项目标识（名称、slug 或项目 ID），在 Modrinth / CurseForge 上查找。未指定 `--from` 时，纯数字先按 CurseForge 项目 ID 查询，其他先查 Modrinth，失败后尝试另一个平台。

整合包版本：Modrinth 接受版本 ID（如 `cZY3Bvs9`）或版本号（如 `14.0.0-beta.2`）；CurseForge 接受 fileId 或文件显示名。不指定时安装最新版本。支持 Modrinth（.mrpack）与 CurseForge（.zip）两种格式，按项目或 Modrinth 直链安装时会同时保存项目图标。

文件夹：取值为启动器内已添加的 Minecraft 文件夹的名称或路径。安装时不指定则使用默认（第一个）文件夹；路径存在但未添加到启动器时也可作为安装目标，不会写入启动器配置。启动时不指定则在所有文件夹中取第一个匹配实例 ID 的实例，指定后只在该文件夹内查找。

实例 ID：启动时按版本 ID（versions 下的目录名）匹配，实例显示名称也可匹配。安装时不指定 `--id` 则自动生成：原版为版本号，带加载器为「版本 加载器-版本」，整合包取包内清单中的名称。

世界与服务器：`launch` 可通过 `--world` 或 `--server` 直接进入世界 / 服务器，两者互斥。进入世界时除实例 ID 与实例文件夹外必须指定世界所在文件夹（saves 下的目录名）——版本隔离下世界与实例不绑定、多个实例可对应同一个世界，同名世界也可能存在多个，因此用文件夹名精确区分。进入服务器时需指定服务器地址，`--port` 缺省为 25565。

### 编码

浏览器链接中的参数值需要百分号编码：空格写作 `%20`，路径中的 `:` 和 `\` 写作 `%3A` 和 `%5C`；作为 `source` 传入的直链需整体编码（其中的 `?`、`&`、`=` 分别为 `%3F`、`%26`、`%3D`）。`+` 号按字面量处理，不会被解释为空格。

命令行中含空格或 `&` 的参数加引号即可，无需编码。

### 其他说明

- CurseForge 的搜索与安装需要 CurseForge API key，构建时通过 `CURSEFORGE_API_KEY` 环境变量嵌入；无 key 的构建仅 Modrinth 可用。
- Windows 下从终端运行时，帮助与错误信息会输出到当前终端。
