# 贡献开发指南

感谢你参与 Portal 开发。本指南说明如何获取完整源码、初始化子模块、配置构建环境并运行项目

## 1. 克隆项目

Portal 使用 git 子模块

```bash
git clone  https://github.com/tiouoo/Portal.git
cd Portal/scripts

# Linux / macOS
./update.sh

# Windows
./update.bat
```

## 2. 准备开发环境

构建 Portal 需要：

- .NET SDK `10.0`
- 开发基岩版相关项目时，需要 C++ 工具链

## 3. 配置环境变量

仓库根目录的 `.env.example` 列出了可选变量。它不会被 .NET 自动加载；请复制其中的变量到操作系统环境、IDE 的运行配置。不要将真实密钥提交到仓库。

常用变量如下：

| 变量                         | 用途                    |
| ---------------------------- | ----------------------- |
| `CURSEFORGE_API_KEY`         | CurseForge API 访问     |
| `MICROSOFT_CLIENT_ID`        | Microsoft/Xbox 登录配置 |
| `GRAVITYCONE_UPTIME_API_KEY` | GravityCone 可用性检测  |
| `CNB_UPDATE_TOKEN`           | CNB 更新源访问          |
| `PRE_MC_KEY`                 | 基岩版 Preview          |
| `REL_MC_KEY`                 | 基岩版 Release          |

Linux/macOS 当前 Shell 会话中可以这样设置：

```bash
export MICROSOFT_CLIENT_ID="your-client-id"
export CURSEFORGE_API_KEY="your-key"
```

PowerShell 中可以这样设置：

```powershell
$env:MICROSOFT_CLIENT_ID = "your-client-id"
$env:CURSEFORGE_API_KEY = "your-key"
```

没有这些可选密钥时，Portal 仍可编译；只有依赖对应服务或构建链的功能会不可用。

## 4. 构建和运行

在仓库根目录执行：

```bash
dotnet build src/Portal.Desktop/Portal.Desktop.csproj
```

如果只修改了某个子项目，也可以直接构建该项目；提交前建议至少构建对应平台的 `Portal.Desktop` 项目。

## 5. 提交修改前检查

```bash
git status
git diff --check
```

请确认没有提交密钥、个人配置、构建产物或未预期的子模块指针变更。涉及界面修改时，除了编译，还应在目标平台实际运行并检查界面效果。

