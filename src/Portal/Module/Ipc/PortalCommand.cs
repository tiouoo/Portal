namespace Portal.Module.Ipc;

public enum PortalCommandKind
{
    DownloadVanilla,
    DownloadLoader,
    DownloadModpack,
    Launch
}

/// <summary>
/// 统一的外部命令模型：命令行参数与 portal:// URI 均解析为该模型，再经命名管道转发或本地执行。
/// </summary>
public sealed class PortalCommand
{
    public PortalCommandKind Kind { get; set; }

    /// <summary>Minecraft 版本号（download vanilla / download loader）。</summary>
    public string? Version { get; set; }

    /// <summary>加载器列表（download loader）。</summary>
    public List<PortalLoaderSpec> Loaders { get; set; } = [];

    /// <summary>整合包来源：本地路径、HTTP(S) 链接，或 Modrinth/CurseForge 的项目名称/slug/ID（install modpack）。</summary>
    public string? Source { get; set; }

    /// <summary>整合包平台（modrinth / curseforge）；按项目安装且不想自动判断时指定。</summary>
    public string? Provider { get; set; }

    /// <summary>整合包版本：Modrinth 的版本 ID/版本号，或 CurseForge 的 fileId；不传则装最新版本。</summary>
    public string? PackVersion { get; set; }

    /// <summary>目标 Minecraft 文件夹：已配置文件夹的名称或路径；不传则用默认/第一个文件夹。</summary>
    public string? Folder { get; set; }

    /// <summary>实例 ID：下载时为自定义安装 ID，启动时为要启动的实例 ID。</summary>
    public string? InstanceId { get; set; }
}

public sealed record PortalLoaderSpec(string Kind, string? Version);
