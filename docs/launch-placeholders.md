# 启动占位符

以下占位符可用于设置 → 高级 中的这些字段，启动时会被替换为实际值：

- Minecraft 窗口标题
- JVM 虚拟机参数
- 启动前执行命令
- 启动后执行命令
- 包装命令

格式统一为 `{name}`。未识别的占位符会原样保留。占位符替换为纯文本，路径含空格时请自行加引号，例如 `--path "{instance_path}"`。

## 实例与路径

| 占位符 | 含义 | 示例 |
| --- | --- | --- |
| `{instance_name}` | 实例显示名称（含备注） | `我的整合包 (1.20.1-forge)` |
| `{instance_id}` | 版本 ID（基岩版为实例名） | `1.20.1-forge-47.2.0` |
| `{instance_path}` | 实例所在路径 | `D:\.minecraft\versions\1.20.1` |
| `{game_dir}` | 游戏工作目录（受版本隔离影响） | `D:\.minecraft\versions\1.20.1` |
| `{minecraft_folder}` | .minecraft 根目录 | `D:\.minecraft` |
| `{natives_dir}` | natives 目录 | `D:\.minecraft\versions\1.20.1\natives` |
| `{assets_dir}` | assets 目录 | `D:\.minecraft\assets` |

## 版本信息

| 占位符 | 含义 | 示例 |
| --- | --- | --- |
| `{version}` | Minecraft 版本号 | `1.20.1` |
| `{version_type}` | 版本类型 | `Release` / `Snapshot` |
| `{loader}` | 模组加载器及版本（原版为空） | `Forge 47.2.0` |
| `{edition}` | 游戏版本类型 | `java` / `bedrock` |

## 账户

| 占位符 | 含义 | 示例 |
| --- | --- | --- |
| `{player_name}` | 玩家名 | `Steve` |
| `{player_uuid}` | 玩家 UUID | `069a79f4-44e9-4726-a5be-fca90e38aaf5` |
| `{account_type}` | 账户类型 | `Microsoft` / `Offline` / `Yggdrasil` |
| `{access_token}` | 本次启动使用的访问令牌 | — |

## Java 与运行参数

基岩版启动时这些值为空。

| 占位符 | 含义 | 示例 |
| --- | --- | --- |
| `{java_path}` | Java 可执行文件路径 | `C:\Java\bin\javaw.exe` |
| `{java_dir}` | Java 所在目录 | `C:\Java\bin` |
| `{java_version}` | Java 完整版本号 | `17.0.8` |
| `{java_major}` | Java 主版本号 | `17` |
| `{width}` | 游戏窗口宽度 | `854` |
| `{height}` | 游戏窗口高度 | `480` |
| `{max_memory}` | 最大内存（MB） | `4096` |
| `{title}` | 解析后的自定义窗口标题 | — |

## 启动器

| 占位符 | 含义 | 示例 |
| --- | --- | --- |
| `{launcher_dir}` | 启动器程序目录 | `D:\Portal` |
| `{launcher_path}` | 启动器可执行文件路径 | `D:\Portal\Portal.exe` |

## 特殊占位符

| 占位符 | 可用位置 | 含义 |
| --- | --- | --- |
| `{process_id}` | 仅“启动后执行命令” | 游戏进程 PID |
| `{command}` | 仅“包装命令” | 完整的 Java 启动命令行（含 Java 路径与全部参数） |

## 各字段行为说明

- **启动前执行命令**：通过系统 Shell（Windows 为 `cmd /c`，其他平台为 `/bin/sh -c`）执行，工作目录为 `{game_dir}`。启动流程会等待命令结束后再继续；退出代码非 0 时仅提示警告，不会中断启动。
- **启动后执行命令**：游戏进程创建后在后台执行，不等待、不影响游戏。可使用 `{process_id}`。
- **包装命令**：若包含 `{command}`，会将其替换为完整 Java 命令行后执行整条命令，例如 `cmd /c {command}`；若不包含，则作为前缀拼接在 Java 命令之前。
- **Minecraft 窗口标题**：仅对 Java 版生效（Windows 下通过持续覆盖窗口标题实现，游戏加载完成重设标题后仍会被覆盖回来）。
