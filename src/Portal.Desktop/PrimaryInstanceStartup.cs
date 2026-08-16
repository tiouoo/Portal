using System.Runtime.InteropServices;
using Portal.Core.App.Service;
using Portal.Core.Module.Ipc;
using Tio.Avalonia.Standard.Modules;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Desktop;

internal static class PrimaryInstanceStartup
{
    public static bool Run(string[] args)
    {
        PortalCommandQueue.Initialize();
        ProtocolRegistration.TryRegisterLinuxOnStartupAsync().GetAwaiter().GetResult();
        PackagePathResolver.TryGetBedrockPackagePath(args, out var packagePath);
        if (packagePath != null)
            App.BedrockPackagePath = packagePath;

        if (PackagePathResolver.TryGetJavaPackagePath(args, out var javaPackagePath))
        {
            var javaCommand = new PortalCommand
            {
                Kind = PortalCommandKind.DownloadModpack,
                Source = javaPackagePath
            };

            if (packagePath == null && PortalCommandService.TryForwardToRunningInstance(javaCommand))
            {
                Logger.Info($"已将 Java 整合包命令转发给正在运行的 Portal 实例：{javaPackagePath}");
                return false;
            }

            App.JavaPackagePath = javaPackagePath;
            if (packagePath == null)
                PortalCommandQueue.Enqueue(javaCommand);
        }

        switch (packagePath)
        {
            case null when javaPackagePath == null && PortalCommandService.TryHandleStartupArgs(args):
                return false;
#if WINDOWS
            case null when javaPackagePath == null && WindowsJumpListService.TryForwardToRunningInstance(args):
                return false;
#endif
            case null:
#if WINDOWS
                WindowsJumpListService.StartCommandServer();
#endif
                break;
        }

#if WINDOWS

        WindowsJumpListService.SetAppUserModelId();
#endif

        if (packagePath == null)
            PortalCommandService.StartCommandServer();

        Logger.Info($"开始启动应用，命令行参数数量：{args.Length}");
        var versionInfo = AppVersionService.Instance.Version;
        Initializer.Program("Portal", "cc.tiouo.Portal", versionInfo.VersionTitle);

#if WINDOWS
        WindowsBedrockFileAssociationService.Register();
        WindowsJavaFileAssociationService.Register();
#endif

        Logger.Info("应用程序启动 Main()");

#if WINDOWS || LINUX
        AppSetup.RegisterBedrockLauncher();
#endif

        LogOperatingSystem();
        return true;
    }

    private static void LogOperatingSystem()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Logger.Info("操作系统：Windows");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Logger.Info("操作系统：Linux");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Logger.Info("操作系统：macOS");
    }
}