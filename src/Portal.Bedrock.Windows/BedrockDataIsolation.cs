using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Reflection;
using BedrockLauncher.Core.CoreOption;
using PeNet;
using Portal.Bedrock.Standard.Manifest;

namespace Portal.Bedrock;

internal static class BedrockDataIsolation
{
    private const string PreloadDllName = "PreloadCpp.dll";
    private const string PreloadResourceName = "Portal.Bedrock.PreloadCpp.dll";

    public static void Prepare(BedrockInstanceConfig config)
    {
        if (!config.EnableIndependentInstance)
            return;

        var gameExecutable = Path.Combine(config.InstancePath, "Minecraft.Windows.exe");
        if (!File.Exists(gameExecutable))
            throw new FileNotFoundException("未找到用于启用数据隔离的基岩版主程序。", gameExecutable);

        File.Copy(ExtractPreloadDll(), Path.Combine(config.InstancePath, PreloadDllName), true);
        WritePreloadConfiguration(config);
        AddPreloadImport(gameExecutable);
    }

    private static string ExtractPreloadDll()
    {
        var nativeFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "xyz.tiouo.Portal", "Native");
        var nativePath = Path.Combine(nativeFolder, PreloadDllName);
        Directory.CreateDirectory(nativeFolder);

        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(PreloadResourceName)
            ?? throw new InvalidOperationException("未找到内嵌的基岩版数据隔离组件。请重新安装 Portal。");
        using var file = new FileStream(nativePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        resource.CopyTo(file);

        return nativePath;
    }

    private static void WritePreloadConfiguration(BedrockInstanceConfig config)
    {
        var configFolder = Path.Combine(config.InstancePath, "config", "Portal");
        Directory.CreateDirectory(configFolder);

        var preloadConfig = new
        {
            config = new
            {
                isConsole = false,
                isVersionIsolated = true,
                isDetailedLog = false
            },
            info = new
            {
                versionType = config.Type == BedrockInstanceReleaseType.Release ? 1 : 0
            }
        };

        var configPath = Path.Combine(configFolder, "config.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(preloadConfig));
    }

    private static void AddPreloadImport(string gameExecutable)
    {
        using var stream = new FileStream(gameExecutable, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        var peFile = new PeFile(stream);

        if (peFile.ImportedFunctions?.Any(import =>
                string.Equals(import.DLL, PreloadDllName, StringComparison.OrdinalIgnoreCase)) == true)
            return;

        peFile.AddImport(PreloadDllName, "Load");
        peFile.Flush();
    }
}
