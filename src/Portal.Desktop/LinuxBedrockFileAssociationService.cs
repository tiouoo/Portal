#if LINUX
using System.Diagnostics;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Desktop;

internal static class LinuxBedrockFileAssociationService
{
    private const string DesktopFileName = "xyz.tiouo.Portal.bedrock-package.desktop";
    private static readonly string[] MimeTypes =
    [
        "application/x-minecraft-mcpack",
        "application/x-minecraft-mcaddon",
        "application/x-minecraft-mcworld",
        "application/x-minecraft-mctemplate"
    ];

    public static void Register()
    {
        try
        {
            var executablePath = Environment.GetEnvironmentVariable("APPIMAGE");
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
                return;

            var dataHome = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var applicationsFolder = Path.Combine(dataHome, "applications");
            var mimePackagesFolder = Path.Combine(dataHome, "mime", "packages");
            Directory.CreateDirectory(applicationsFolder);
            Directory.CreateDirectory(mimePackagesFolder);

            File.WriteAllText(Path.Combine(mimePackagesFolder, "xyz.tiouo.Portal.bedrock-packages.xml"),
                CreateMimeDefinition());
            File.WriteAllText(Path.Combine(applicationsFolder, DesktopFileName), CreateDesktopFile(executablePath));

            Run("update-mime-database", Path.Combine(dataHome, "mime"));
            Run("update-desktop-database", applicationsFolder);
            foreach (var mimeType in MimeTypes)
                Run("xdg-mime", "default", DesktopFileName, mimeType);
        }
        catch (Exception exception)
        {
            Logger.Error($"注册基岩版包文件关联失败：{exception}");
        }
    }

    private static string CreateDesktopFile(string executablePath) =>
        $"""
        [Desktop Entry]
        Type=Application
        Name=Portal
        Comment=Import Minecraft Bedrock packages
        Exec="{executablePath.Replace("\"", "\\\"")}" %f
        Terminal=false
        NoDisplay=true
        MimeType={string.Join(';', MimeTypes)};

        """;

    private static string CreateMimeDefinition() =>
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
          <mime-type type="application/x-minecraft-mcpack">
            <comment>Minecraft Bedrock resource pack</comment>
            <glob pattern="*.mcpack"/>
          </mime-type>
          <mime-type type="application/x-minecraft-mcaddon">
            <comment>Minecraft Bedrock add-on</comment>
            <glob pattern="*.mcaddon"/>
          </mime-type>
          <mime-type type="application/x-minecraft-mcworld">
            <comment>Minecraft Bedrock world</comment>
            <glob pattern="*.mcworld"/>
          </mime-type>
          <mime-type type="application/x-minecraft-mctemplate">
            <comment>Minecraft Bedrock world template</comment>
            <glob pattern="*.mctemplate"/>
          </mime-type>
        </mime-info>
        """;

    private static void Run(string fileName, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo { FileName = fileName, UseShellExecute = false };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo);
            process?.WaitForExit(5000);
        }
        catch
        {
            // Desktop database tools are optional; the files remain available for the next system refresh.
        }
    }
}
#endif
