namespace Portal.Core.Classes.Config;

/// <summary>
/// Portal 的资源/模组文件下载源选择策略。数值与 Iridium 的
/// <see cref="Iridium.Enums.SourceSelectionMode"/> 一致，便于直接映射。
/// </summary>
public enum ResourceDownloadSourceMode
{
    /// <summary>自动选择：延迟探测后选用较快的源。</summary>
    Auto = 0,

    /// <summary>官方优先：先尝试官方，失败后回退镜像。</summary>
    OfficialPreferred = 1,

    /// <summary>镜像优先：先尝试镜像，失败后回退官方。</summary>
    MirrorPreferred = 2,

    /// <summary>仅使用官方源，绝不使用镜像。</summary>
    OfficialOnly = 3
}
