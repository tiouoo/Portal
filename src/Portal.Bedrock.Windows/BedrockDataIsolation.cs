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
    private const string PreloadDllName = "PreloadCpp.dll"; //PreloadCpp.dll
    private const string PreloadResourceName = "PreloadCpp.dll";
    private const string FallbackPreloadDllPrefix = "P";

    public static void Prepare(BedrockInstanceConfig config)
    {
        var gameExecutable = Path.Combine(config.InstancePath, "Minecraft.Windows.exe");
        if (!File.Exists(gameExecutable))
            throw new FileNotFoundException("未找到用于启用数据隔离的基岩版主程序。", gameExecutable);

        var currentDllName = GetPreloadImportName(gameExecutable) ?? PreloadDllName;
        CleanupUnusedFallbackDlls(config.InstancePath, currentDllName);
        var preloadDllName = DeployPreloadDll(config.InstancePath, currentDllName);
        WritePreloadConfiguration(config);
        try
        {
            AddPreloadImport(gameExecutable, preloadDllName);
            CleanupUnusedFallbackDlls(config.InstancePath, preloadDllName);
        }
        catch
        {
            // The fallback was never referenced if the PE import update failed.
            CleanupUnusedFallbackDlls(config.InstancePath, currentDllName);
            throw;
        }
    }

    private static string DeployPreloadDll(string instancePath, string currentDllName)
    {
        var sourcePath = ExtractPreloadDll();

        try
        {
            File.Copy(sourcePath, Path.Combine(instancePath, currentDllName), true);
            return currentDllName;
        }
        catch (IOException)
        {
            var fallbackDllName = CreateFallbackDllName(instancePath);
            File.Copy(sourcePath, Path.Combine(instancePath, fallbackDllName));
            return fallbackDllName;
        }
    }

    private static string ExtractPreloadDll()
    {
        var nativeFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "xyz.tiouo.Portal", "Native");
        var nativePath = Path.Combine(nativeFolder, PreloadDllName);
        Directory.CreateDirectory(nativeFolder);

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(PreloadResourceName)
                           ?? throw new InvalidOperationException("未找到内嵌的基岩版数据隔离组件。请重新安装 Portal。");

        using var file = new FileStream(nativePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        stream.CopyTo(file);

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
                // Native Windows sharing must bypass the file hooks entirely.
                isVersionIsolated = !config.EnableLauncherSharedData,
                isDetailedLog = false,
                folderPolicyString = GetFolderPolicy(config)
            },
            info = new
            {
                versionType = config.Type == BedrockInstanceReleaseType.Release ? 1 : 0
            }
        };

        var configPath = Path.Combine(configFolder, "config.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(preloadConfig));
    }

    private static string GetFolderPolicy(BedrockInstanceConfig config)
    {
        if (config.EnableIndependentInstance)
            return config.EnableLauncherSharedData ? "shares" : "independence";

        return config.EnableLauncherSharedData ? "native" : "portal";
    }

    private static string? GetPreloadImportName(string gameExecutable)
    {
        using var stream = new FileStream(gameExecutable, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var peFile = new PeFile(stream);

        return peFile.ImportedFunctions?
            .Select(import => import.DLL)
            .FirstOrDefault(IsPreloadDllName);
    }

    private static void AddPreloadImport(string gameExecutable, string preloadDllName)
    {
        using var stream = new FileStream(gameExecutable, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        var peFile = new PeFile(stream);

        var currentDllName = peFile.ImportedFunctions?
            .Select(import => import.DLL)
            .FirstOrDefault(IsPreloadDllName);

        if (currentDllName == null)
        {
            peFile.AddImport(preloadDllName, "Load");
            peFile.Flush();
            return;
        }

        if (string.Equals(currentDllName, preloadDllName, StringComparison.OrdinalIgnoreCase))
            return;

        var descriptor = peFile.ImageImportDescriptors?
            .FirstOrDefault(item => string.Equals(
                peFile.RawFile.ReadAsciiString(item.Name.RvaToOffset(peFile.ImageSectionHeaders!)),
                currentDllName, StringComparison.OrdinalIgnoreCase));
        if (descriptor == null)
            throw new InvalidDataException("无法更新基岩版数据隔离组件的 DLL 导入项。");

        var originalNameLength = currentDllName.Length;
        if (preloadDllName.Length > originalNameLength)
            throw new InvalidOperationException("备用数据隔离组件名称超过 PE 导入项可用长度。");

        var nameBuffer = new byte[originalNameLength + 1];
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(preloadDllName);
        Array.Copy(nameBytes, nameBuffer, nameBytes.Length);
        peFile.RawFile.WriteBytes(descriptor.Name.RvaToOffset(peFile.ImageSectionHeaders!), nameBuffer);
        peFile.Flush();
    }

    private static string CreateFallbackDllName(string instancePath)
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var name = $"{FallbackPreloadDllPrefix}{Guid.NewGuid():N}"[..9] + ".dll";
            if (!File.Exists(Path.Combine(instancePath, name)))
                return name;
        }

        throw new IOException("无法创建可用的数据隔离组件备用文件。");
    }

    private static void CleanupUnusedFallbackDlls(string instancePath, string activeDllName)
    {
        foreach (var path in Directory.EnumerateFiles(instancePath, $"{FallbackPreloadDllPrefix}????????.dll"))
        {
            if (string.Equals(Path.GetFileName(path), activeDllName, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A running game still has this DLL loaded; remove it on a later launch.
            }
            catch (UnauthorizedAccessException)
            {
                // Leave files protected by the operating system untouched.
            }
        }
    }

    private static bool IsPreloadDllName(string name) =>
        string.Equals(name, PreloadDllName, StringComparison.OrdinalIgnoreCase) ||
        name.Length == 13 && name.StartsWith(FallbackPreloadDllPrefix, StringComparison.OrdinalIgnoreCase) &&
        name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
        name[1..9].All(Uri.IsHexDigit);
}
