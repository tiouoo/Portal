using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Classes.Entries;

public class ConfigIdentifyExtension
{
    public static void MinecraftFolder(ConfigEntry entry)
    {
        var traditionalFolders = entry.MinecraftFolders.Where(IsTraditionalFolder).ToList();
        if (traditionalFolders.Count == 0)
        {
            entry.DefaultMinecraftFolder = null;
            var defaultFolder = CreateDefaultMinecraftFolder();
            entry.MinecraftFolders.Insert(0, defaultFolder);
            traditionalFolders.Add(defaultFolder);
        }

        if (entry.DefaultMinecraftFolder == null ||
            !entry.MinecraftFolders.Contains(entry.DefaultMinecraftFolder) ||
            !IsTraditionalFolder(entry.DefaultMinecraftFolder))
        {
            entry.DefaultMinecraftFolder = traditionalFolders[0];
        }
    }

    private static bool IsTraditionalFolder(MinecraftFolderEntry folder)
    {
        return folder.DetectedLayout.Kind == MinecraftFolderKind.Standard;
    }

    private static MinecraftFolderEntry CreateDefaultMinecraftFolder()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "portal.minecraft");
        Helper.TryCreateFolder(path);
        return new MinecraftFolderEntry
        {
            FolderName = "Portal 默认文件夹",
            FolderPath = path,
            FolderKind = MinecraftFolderKind.Standard
        };
    }
}
