#if WINDOWS
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Desktop;

internal static partial class WindowsBedrockFileAssociationService
{
    private const uint AssociationChanged = 0x08000000;
    private const uint IdList = 0x0000;

    private static readonly (string Extension, string ProgId, string TypeName)[] Associations =
    [
        (".mcpack", "Portal.Minecraft.Mcpack", CommonLanguageManager.Instance.desktop_bedrockFileAssociation_mcpack.CurrentValue()),
        (".mcaddon", "Portal.Minecraft.Mcaddon", CommonLanguageManager.Instance.desktop_bedrockFileAssociation_mcaddon.CurrentValue()),
        (".mcworld", "Portal.Minecraft.Mcworld", CommonLanguageManager.Instance.desktop_bedrockFileAssociation_mcworld.CurrentValue()),
        (".mctemplate", "Portal.Minecraft.Mctemplate", CommonLanguageManager.Instance.desktop_bedrockFileAssociation_mctemplate.CurrentValue())
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
                application.SetValue("ApplicationDescription", CommonLanguageManager.Instance.desktop_fileAssociation_appDescription.CurrentValue());
                using var icon = progId.CreateSubKey("DefaultIcon");
                icon.SetValue(null, $"\"{executablePath}\",0");
                using var command = progId.CreateSubKey(@"shell\open\command");
                command.SetValue(null, $"\"{executablePath}\" \"%1\"");
            }

            using (var application = classes.CreateSubKey($@"Applications\{executableName}"))
            {
                application.SetValue("FriendlyAppName", "Portal");
                using var supportedTypes = application.CreateSubKey("SupportedTypes");
                foreach (var extension in BedrockPackageImportService.SupportedExtensions)
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
            Logger.Error(string.Format(LogLanguageManager.Instance.desktop_bedrockFileAssociation_registerFailed.CurrentValue(), exception));
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
#endif