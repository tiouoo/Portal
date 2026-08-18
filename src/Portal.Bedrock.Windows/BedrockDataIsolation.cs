using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Reflection;
using System.Security.Cryptography;
using PeNet;
using Portal.Bedrock.Standard.Manifest;
using Portal.Bedrock.Standard.Interface;

namespace Portal.Bedrock;

internal static class BedrockDataIsolation
{
    private const string PreloadDllName = "Portal.Preload.dll";
    private const string PreviousPreloadDllName = "Portal.Preload.Net.dll";
    private const string PreloadResourceName = "Portal.Preload.dll";
    private const string FallbackPreloadDllPrefix = "P";
    private const string LegacyPreloadHookName = "XUserHook.dll";

    public static string Prepare(BedrockInstanceConfig config, Action<string, BedrockLogLevel>? log = null)
    {
        var gameExecutable = Path.Combine(config.InstancePath, "Minecraft.Windows.exe");
        if (!File.Exists(gameExecutable))
            throw new FileNotFoundException("未找到用于启用数据隔离的基岩版主程序。", gameExecutable);

        log?.Invoke($"准备基岩版数据隔离：{gameExecutable}", BedrockLogLevel.Information);
        SyncPreloadMods(config, log);
        var preloadDllName = DeployPreloadDll(config.InstancePath);
        var nativeLogPath = WritePreloadConfiguration(config);
        try
        {
            AddPreloadImport(gameExecutable, preloadDllName);
            CleanupStalePreloadArtifacts(config.InstancePath, preloadDllName, log);
        }
        catch
        {
            CleanupStalePreloadArtifacts(config.InstancePath, preloadDllName, log);
            throw;
        }
        return nativeLogPath;
    }

    private static void SyncPreloadMods(BedrockInstanceConfig config, Action<string, BedrockLogLevel>? log)
    {
        var runtimeFolder = Path.Combine(config.InstancePath, "preload", "Portal");
        Directory.CreateDirectory(runtimeFolder);
        var activeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var preloadMods = BedrockModManager.Scan(config).Where(mod => mod.Config.Enabled && mod.Config.Preload).ToArray();
        log?.Invoke($"发现 {preloadMods.Length} 个已启用的预加载模组", BedrockLogLevel.Information);
        foreach (var mod in preloadMods)
        {
            using var stream = File.OpenRead(mod.FilePath);
            var fileName = $"{Convert.ToHexString(SHA256.HashData(stream))[..16]}.dll";
            var destination = Path.Combine(runtimeFolder, fileName);
            activeFiles.Add(fileName);
            if (!File.Exists(destination))
                File.Copy(mod.FilePath, destination);
            log?.Invoke($"已准备预加载模组：{mod.FileName}", BedrockLogLevel.Information);
        }

        var manifestPath = Path.Combine(runtimeFolder, "mods.txt");
        var temporaryPath = manifestPath + ".tmp";
        File.WriteAllLines(temporaryPath, activeFiles.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        File.Move(temporaryPath, manifestPath, true);
        foreach (var path in Directory.EnumerateFiles(runtimeFolder, "*.dll"))
        {
            if (activeFiles.Contains(Path.GetFileName(path))) continue;
            try { File.Delete(path); }
            catch (IOException exception) { log?.Invoke($"删除过期预加载模组失败：{path}，{exception}", BedrockLogLevel.Warning); }
            catch (UnauthorizedAccessException exception) { log?.Invoke($"删除过期预加载模组被拒绝：{path}，{exception}", BedrockLogLevel.Warning); }
        }
    }

    private static string DeployPreloadDll(string instancePath)
    {
        var sourcePath = ExtractPreloadDll();

        try
        {
            File.Copy(sourcePath, Path.Combine(instancePath, PreloadDllName), true);
            return PreloadDllName;
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
            "cc.tiouo.Portal", "Native");
        var nativePath = Path.Combine(nativeFolder, PreloadDllName);
        Directory.CreateDirectory(nativeFolder);

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(PreloadResourceName)
                           ?? throw new InvalidOperationException("未找到内嵌的基岩版数据隔离组件。请重新安装 Portal。");

        using var file = new FileStream(nativePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        stream.CopyTo(file);

        return nativePath;
    }

    private static string WritePreloadConfiguration(BedrockInstanceConfig config)
    {
        var configFolder = Path.Combine(config.InstancePath, "config", "Portal");
        Directory.CreateDirectory(configFolder);
        var logFolder = Path.Combine(configFolder, "logs");
        Directory.CreateDirectory(logFolder);
        var nativeLogFile = $"native-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log";

        var preloadConfig = new
        {
            config = new
            {
                isConsole = false,
                
                isVersionIsolated = config.BuildType == BedrockBuildType.GDK && !config.EnableLauncherSharedData,
                isDetailedLog = false,
                folderPolicyString = GetFolderPolicy(config),
                nativeLogFile
            },
            info = new
            {
                versionType = config.Type == BedrockInstanceReleaseType.Release ? 1 : 0
            }
        };

        var configPath = Path.Combine(configFolder, "config.json");
        Trace.TraceInformation($"写入基岩版数据隔离配置：{configPath}。");
        File.WriteAllText(configPath, JsonSerializer.Serialize(preloadConfig));
        return Path.Combine(logFolder, nativeLogFile);
    }

    private static string GetFolderPolicy(BedrockInstanceConfig config)
    {
        if (config.BuildType == BedrockBuildType.UWP)
            return "native";
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
        try
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
                .FirstOrDefault(item =>
                    item.Name != 0 &&
                    string.Equals(
                        peFile.RawFile.ReadAsciiString(item.Name.RvaToOffset(peFile.ImageSectionHeaders!)),
                        currentDllName, StringComparison.OrdinalIgnoreCase));
            if (descriptor == null)
                throw new InvalidDataException("无法更新基岩版数据隔离组件的 DLL 导入项。");

            var originalNameLength = currentDllName.Length;
            if (preloadDllName.Length > originalNameLength)
                throw new InvalidOperationException(
                    $"无法将预加载组件导入名改为 {preloadDllName}：超出 PE 导入项可用长度。");

            var nameBuffer = new byte[originalNameLength + 1];
            var nameBytes = System.Text.Encoding.ASCII.GetBytes(preloadDllName);
            Array.Copy(nameBytes, nameBuffer, nameBytes.Length);
            peFile.RawFile.WriteBytes(descriptor.Name.RvaToOffset(peFile.ImageSectionHeaders!), nameBuffer);
            peFile.Flush();
        }
        catch (Exception exception) when (exception is not InvalidOperationException and not InvalidDataException)
        {
            throw new InvalidOperationException(
                $"无法修补基岩版游戏导入表（{preloadDllName}）。游戏可能已损坏，请重新安装该版本。", exception);
        }
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
            var fileName = Path.GetFileName(path);
            
            
            if (!IsPreloadDllName(fileName) ||
                string.Equals(fileName, activeDllName, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                File.Delete(path);
            }
            catch (IOException exception)
            {
                
                Trace.TraceError($"删除过期基岩版数据隔离组件失败：{path}{Environment.NewLine}{exception}");
            }
            catch (UnauthorizedAccessException exception)
            {
                
                Trace.TraceError($"删除过期基岩版数据隔离组件被拒绝：{path}{Environment.NewLine}{exception}");
            }
        }
    }

    private static bool IsPreloadDllName(string name) =>
        string.Equals(name, PreloadDllName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, PreviousPreloadDllName, StringComparison.OrdinalIgnoreCase) ||
        name.Length == 13 && name.StartsWith(FallbackPreloadDllPrefix, StringComparison.OrdinalIgnoreCase) &&
        name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
        name[1..9].All(Uri.IsHexDigit);

    private static void CleanupStalePreloadArtifacts(string instancePath, string activePreloadName,
        Action<string, BedrockLogLevel>? log = null)
    {
        CleanupUnusedFallbackDlls(instancePath, activePreloadName);

        foreach (var stale in new[] { PreviousPreloadDllName, "PreloadCpp.dll" })
        {
            if (string.Equals(stale, activePreloadName, StringComparison.OrdinalIgnoreCase))
                continue;
            var stalePath = Path.Combine(instancePath, stale);
            try
            {
                if (File.Exists(stalePath))
                    File.Delete(stalePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                log?.Invoke($"删除过期的预加载组件失败：{stalePath}，{exception}", BedrockLogLevel.Warning);
            }
        }

        var staleHookPath = Path.Combine(instancePath, "preload", LegacyPreloadHookName);
        try
        {
            if (File.Exists(staleHookPath))
                File.Delete(staleHookPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"删除过期的 Xbox 账户 Hook 失败：{staleHookPath}，{exception}", BedrockLogLevel.Warning);
        }
    }
}
