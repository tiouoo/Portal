#if WINDOWS
using Microsoft.Win32;
using System.Linq;
using System.Runtime.InteropServices;

namespace Portal.Desktop;

internal static class WindowsJavaFileAssociationService
{
    private const uint AssociationChanged = 0x08000000;
    private const uint IdList = 0x0000;

    private static readonly (string Extension, string ProgId, string TypeName)[] Associations =
    [
        (".mrpack", "Portal.Minecraft.Mrpack", "Minecraft 整合包")
    ];

    public static void Register()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
                return;
            var executableName = Path.GetFileName(executablePath);

            using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
            foreach (var association in Associations)
            {
                using var progId = classes.CreateSubKey(association.ProgId);
                progId.SetValue(null, association.TypeName);
                progId.SetValue("FriendlyTypeName", association.TypeName);
                using var application = progId.CreateSubKey("Application");
                application.SetValue("ApplicationName", "Portal");
                application.SetValue("ApplicationDescription", "Portal Minecraft 启动器");
                using var icon = progId.CreateSubKey("DefaultIcon");
                icon.SetValue(null, $"\"{executablePath}\",0");
                using var command = progId.CreateSubKey(@"shell\open\command");
                command.SetValue(null, $"\"{executablePath}\" \"%1\"");
            }

            using (var application = classes.CreateSubKey($@"Applications\{executableName}"))
            {
                application.SetValue("FriendlyAppName", "Portal");
                using var supportedTypes = application.CreateSubKey("SupportedTypes");
                foreach (var extension in Associations.Select(a => a.Extension))
                    supportedTypes.SetValue(extension, string.Empty);
                using var command = application.CreateSubKey(@"shell\open\command");
                command.SetValue(null, $"\"{executablePath}\" \"%1\"");
            }

            foreach (var association in Associations)
            {
                using var extensionKey = classes.CreateSubKey(association.Extension);
                extensionKey.SetValue(null, association.ProgId);
                extensionKey.SetValue("Content Type", "application/zip");
                using var openWith = extensionKey.CreateSubKey("OpenWithProgids");
                openWith.SetValue(association.ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            }

            SHChangeNotify(AssociationChanged, IdList, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception exception)
        {
            Tio.Avalonia.Standard.Modules.DiskIO.Logger.Error($"注册整合包文件关联失败：{exception}");
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
#endif