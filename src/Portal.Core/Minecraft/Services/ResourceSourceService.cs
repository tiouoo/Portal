using Iridium.Download;
using Iridium.Enums;
using Iridium.Helpers.Resources;
using Iridium.Interfaces.Resources;
using Portal.Core.Classes.Config;
using Portal.Core.Const;

namespace Portal.Core.Minecraft.Services;

/// <summary>
/// 把 Portal 的下载源设置映射到 Iridium 的下载源自动选择（<see cref="SourceSelector"/>）。
/// </summary>
public static class ResourceSourceService
{
    /// <summary>
    /// 当前活跃的资源文件镜像源。临时方案使用「天跑」；等 MCIM Files 恢复后替换为 McimResourceMirror。
    /// </summary>
    public static IResourceMirror ActiveResourceMirror { get; } = new TianpaoResourceMirror();

    /// <summary>
    /// 注册活跃镜像源并从配置应用当前下载源模式。应用启动时调用一次。
    /// </summary>
    public static void Initialize()
    {
        SourceSelector.ResourceMirror = ActiveResourceMirror;
        Apply(Data.ConfigEntry.ResourceDownloadSource);
    }

    public static void Apply(ResourceDownloadSourceMode mode)
    {
        SourceSelector.Configure((SourceSelectionMode)(int)mode);
    }
}
