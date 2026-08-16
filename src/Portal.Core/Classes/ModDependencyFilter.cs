using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Classes;

public static class ModDependencyFilter
{
        public static async Task<IReadOnlyList<ModVersionFileItem>> FilterInstalledAsync(
        MinecraftInstance instance, IReadOnlyList<ModVersionFileItem> dependencies)
    {
        if (dependencies.Count == 0) return dependencies;

        IReadOnlyList<ModInfo> installed;
        try
        {
            installed = await new ModService().ScanAsync(instance);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[ModDownload] 扫描已安装模组失败，依赖将照常下载：{exception}");
            return dependencies;
        }

        var installedProjects = installed
            .Where(mod => !string.IsNullOrWhiteSpace(mod.ProjectId))
            .Select(mod => mod.ProjectId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installedNames = installed
            .Select(mod => Path.GetFileNameWithoutExtension(mod.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var pending = new List<ModVersionFileItem>();
        var queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in dependencies)
        {
            var key = string.IsNullOrWhiteSpace(dependency.ProjectId)
                ? $"{dependency.Source}:{dependency.Id}"
                : $"{dependency.Source}:{dependency.ProjectId}";
            if (!queued.Add(key)) continue;

            if (!string.IsNullOrWhiteSpace(dependency.ProjectId) &&
                installedProjects.Contains(dependency.ProjectId)) continue;
            if (installedNames.Contains(Path.GetFileNameWithoutExtension(dependency.FileName))) continue;

            pending.Add(dependency);
        }

        return pending;
    }
}
