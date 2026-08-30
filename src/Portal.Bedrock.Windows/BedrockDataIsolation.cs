using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PeNet;
using Portal.Bedrock.Standard.Manifest;
using Portal.Bedrock.Standard.Interface;
using Portal.Localization;

namespace Portal.Bedrock;

internal static class BedrockDataIsolation
{
    private const string PreloadDllName = "Portal.Preload.dll";
    private const string PreviousPreloadDllName = "Portal.Preload.Net.dll";
    private const string PreloadResourceName = "Portal.Preload.dll";
    private const string FallbackPreloadDllPrefix = "P";
    private const string LegacyPreloadHookName = "XUserHook.dll";
    private const string BootstrapDllName = "Portal.Bootstrap.dll";
    private const string OriginalExecutableName = "Minecraft.Windows.portal-original.exe";

    public static async Task<string> PrepareAsync(BedrockInstanceConfig config,
        Action<string, BedrockLogLevel>? log = null, CancellationToken cancellationToken = default)
    {
        var gameExecutable = Path.Combine(config.InstancePath, "Minecraft.Windows.exe");
        if (!File.Exists(gameExecutable))
            throw new FileNotFoundException(CommonLanguageManager.Instance.bedrockLaunch_mainExecutableNotFound.CurrentValue(), gameExecutable);

        cancellationToken.ThrowIfCancellationRequested();
        log?.Invoke(string.Format(LogLanguageManager.Instance.bedrock_preparingDataIsolation.CurrentValue(), gameExecutable), BedrockLogLevel.Information);
        EnsureOriginalExecutable(config.InstancePath, gameExecutable);
        var leviLaminaPreloader = BedrockModManager.PrepareLeviLaminaPreloader(config);
        var requiresLeviLaminaPatch = leviLaminaPreloader is not null;
        if (leviLaminaPreloader is not null)
        {
            var rootPreloader = Path.Combine(config.InstancePath, "PreLoader.dll");
            if (!string.Equals(Path.GetFullPath(leviLaminaPreloader), Path.GetFullPath(rootPreloader),
                    StringComparison.OrdinalIgnoreCase))
                File.Copy(leviLaminaPreloader, rootPreloader, true);
        }
        if (!HasImportedDll(gameExecutable, BootstrapDllName) ||
            HasImportedDll(gameExecutable, "PreLoader.dll") != requiresLeviLaminaPatch)
        {
            RestoreOriginalExecutable(config.InstancePath, gameExecutable);
            if (requiresLeviLaminaPatch)
                await ApplyLeviLaminaPatchAsync(config, gameExecutable, log, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            AddBootstrapImport(gameExecutable);
        }

        PrepareLaunchInfo(config, log);
        SyncPreloadMods(config, log);
        var preloadDllName = DeployPreloadDll(config.InstancePath);
        DeployBootstrapDll(config.InstancePath);
        var nativeLogPath = WritePreloadConfiguration(config);
        try
        {
            AddBootstrapImport(gameExecutable);
            CleanupStalePreloadArtifacts(config.InstancePath, preloadDllName, log);
        }
        catch
        {
            CleanupStalePreloadArtifacts(config.InstancePath, preloadDllName, log);
            throw;
        }
        return nativeLogPath;
    }

    private static void EnsureOriginalExecutable(string instancePath, string gameExecutable)
    {
        var originalPath = Path.Combine(instancePath, "config", "Portal", OriginalExecutableName);
        if (File.Exists(originalPath))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
        File.Copy(gameExecutable, originalPath, false);
    }

    private static bool HasImportedDll(string gameExecutable, string dllName)
    {
        using var stream = new FileStream(gameExecutable, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var peFile = new PeFile(stream);
        return peFile.ImportedFunctions?.Any(import =>
            string.Equals(import.DLL, dllName, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static void RestoreOriginalExecutable(string instancePath, string gameExecutable)
    {
        var originalPath = Path.Combine(instancePath, "config", "Portal", OriginalExecutableName);
        File.Copy(originalPath, gameExecutable, true);
    }

    private static async Task ApplyLeviLaminaPatchAsync(BedrockInstanceConfig config, string gameExecutable,
        Action<string, BedrockLogLevel>? log, CancellationToken cancellationToken)
    {
        var peEditor = BedrockModManager.GetLeviLaminaPeEditor(config);
        if (peEditor is null)
            throw new FileNotFoundException(
                LogLanguageManager.Instance.bedrockLaunch_leviLaminaPeEditorMissing.CurrentValue(),
                Path.Combine(config.InstancePath, "PeEditor.exe"));

        log?.Invoke(LogLanguageManager.Instance.bedrockLaunch_applyingLeviLaminaPatch.CurrentValue(),
            BedrockLogLevel.Information);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = peEditor,
                WorkingDirectory = config.InstancePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("-mb");
        process.StartInfo.ArgumentList.Add("--exe");
        process.StartInfo.ArgumentList.Add(Path.GetFileName(gameExecutable));
        process.StartInfo.ArgumentList.Add("--inplace");

        if (!process.Start())
            throw new InvalidOperationException(
                LogLanguageManager.Instance.bedrockLaunch_leviLaminaPatchStartFailed.CurrentValue());

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch (InvalidOperationException)
            {
            }
            throw;
        }

        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        if (process.ExitCode != 0 || !HasImportedDll(gameExecutable, "PreLoader.dll"))
        {
            var details = string.Join(Environment.NewLine, new[] { output, error }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            throw new InvalidOperationException(string.Format(
                LogLanguageManager.Instance.bedrockLaunch_leviLaminaPatchFailed.CurrentValue(),
                process.ExitCode, details));
        }

        log?.Invoke(LogLanguageManager.Instance.bedrockLaunch_leviLaminaPatchComplete.CurrentValue(),
            BedrockLogLevel.Information);
    }

    private static void RepairDuplicateImportSections(string gameExecutable,
        Action<string, BedrockLogLevel>? log)
    {
        using var stream = new FileStream(gameExecutable, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        var peFile = new PeFile(stream);
        var sections = peFile.ImageSectionHeaders?.Where(section => section.Name == ".addImp").ToArray();
        if (sections is not { Length: 2 } || peFile.ImageNtHeaders == null)
            return;

        var first = sections[0];
        var importDirectory = peFile.ImageNtHeaders.OptionalHeader.DataDirectory[(int)PeNet.Header.Pe.DataDirectoryType.Import];
        if (importDirectory.VirtualAddress != sections[1].VirtualAddress)
            return;
        importDirectory.VirtualAddress = first.VirtualAddress;
        importDirectory.Size -= 0x14;
        peFile.Flush();
        log?.Invoke("Repaired duplicate Portal import sections from an interrupted launch preparation.",
            BedrockLogLevel.Warning);
    }

    private static void DeployBootstrapDll(string instancePath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(BootstrapDllName)
                           ?? throw new InvalidOperationException("Portal.Bedrock.Windows does not include Portal.Bootstrap.dll.");
        using var file = new FileStream(Path.Combine(instancePath, BootstrapDllName), FileMode.Create,
            FileAccess.Write, FileShare.Read);
        stream.CopyTo(file);
    }

    private static void PrepareLaunchInfo(BedrockInstanceConfig config, Action<string, BedrockLogLevel>? log)
    {
        try
        {
            var outputDirectory = Path.Combine(config.InstancePath, "config", "Portal", "launch-info");
            Directory.CreateDirectory(outputDirectory);

            foreach (var staleFile in Directory.EnumerateFiles(outputDirectory, "*.lang"))
                File.Delete(staleFile);

            if (!config.EnableLaunchInfo)
                return;

            var textsDirectory = Path.Combine(config.InstancePath, "data", "resource_packs", "vanilla", "texts");
            if (!Directory.Exists(textsDirectory))
                return;

            var version = string.IsNullOrWhiteSpace(config.LauncherVersion)
                ? "local-build"
                : config.LauncherVersion.Trim().Replace('\r', ' ').Replace('\n', ' ');
            var launchText = $"©Mojang AB· Portal {version}";
            var languageCount = 0;
            foreach (var sourcePath in Directory.EnumerateFiles(textsDirectory, "*.lang", SearchOption.TopDirectoryOnly))
            {
                var bytes = File.ReadAllBytes(sourcePath);
                var rewritten = RewriteCopyright(bytes, launchText);
                File.WriteAllBytes(Path.Combine(outputDirectory, Path.GetFileName(sourcePath)), rewritten);
                languageCount++;
            }

            log?.Invoke($"Prepared Portal launch information for {languageCount} languages.",
                BedrockLogLevel.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"Portal launch information is unavailable: {exception.Message}", BedrockLogLevel.Warning);
        }
    }

    private static byte[] RewriteCopyright(byte[] input, string launchText)
    {
        var hasBom = input.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF });
        var offset = hasBom ? 3 : 0;
        var content = System.Text.Encoding.UTF8.GetString(input, offset, input.Length - offset);
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var found = false;
        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].StartsWith("menu.copyright=", StringComparison.Ordinal))
                continue;
            lines[index] = $"menu.copyright={launchText}";
            found = true;
        }

        if (!found)
            content = string.Join(newline, lines).TrimEnd('\r', '\n') + newline + $"menu.copyright={launchText}" + newline;
        else
            content = string.Join(newline, lines);

        var body = System.Text.Encoding.UTF8.GetBytes(content);
        if (!hasBom)
            return body;
        var result = new byte[body.Length + 3];
        result[0] = 0xEF;
        result[1] = 0xBB;
        result[2] = 0xBF;
        body.CopyTo(result, 3);
        return result;
    }

    private static void SyncPreloadMods(BedrockInstanceConfig config, Action<string, BedrockLogLevel>? log)
    {
        var preloadFolder = Path.Combine(config.InstancePath, "preload");
        Directory.CreateDirectory(preloadFolder);
        var preloaderDestination = Path.Combine(preloadFolder, "PreLoader.dll");
        if (File.Exists(preloaderDestination))
            File.Delete(preloaderDestination);

        var runtimeFolder = Path.Combine(config.InstancePath, "preload", "Portal");
        Directory.CreateDirectory(runtimeFolder);
        var activeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var preloadMods = BedrockModManager.Scan(config).Where(mod => mod.Config.Enabled && mod.Config.Preload).ToArray();
        log?.Invoke(string.Format(LogLanguageManager.Instance.bedrock_foundEnabledPreloadMods.CurrentValue(), preloadMods.Length), BedrockLogLevel.Information);
        foreach (var mod in preloadMods)
        {
            using var stream = File.OpenRead(mod.FilePath);
            var fileName = $"{Convert.ToHexString(SHA256.HashData(stream))[..16]}.dll";
            var destination = Path.Combine(runtimeFolder, fileName);
            activeFiles.Add(fileName);
            if (!File.Exists(destination))
                File.Copy(mod.FilePath, destination);
            log?.Invoke(string.Format(LogLanguageManager.Instance.bedrock_preloadModReady.CurrentValue(), mod.FileName), BedrockLogLevel.Information);
        }

        var manifestPath = Path.Combine(runtimeFolder, "mods.txt");
        var temporaryPath = manifestPath + ".tmp";
        File.WriteAllLines(temporaryPath, activeFiles.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        File.Move(temporaryPath, manifestPath, true);
        foreach (var path in Directory.EnumerateFiles(runtimeFolder, "*.dll"))
        {
            if (activeFiles.Contains(Path.GetFileName(path))) continue;
            try { File.Delete(path); }
            catch (IOException exception) { log?.Invoke(string.Format(LogLanguageManager.Instance.bedrock_deleteStalePreloadModFailed.CurrentValue(), path, exception), BedrockLogLevel.Warning); }
            catch (UnauthorizedAccessException exception) { log?.Invoke(string.Format(LogLanguageManager.Instance.bedrock_deleteStalePreloadModDenied.CurrentValue(), path, exception), BedrockLogLevel.Warning); }
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
                           ?? throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockLaunch_missingEmbeddedIsolationComponent.CurrentValue());

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
                launchInfoEnabled = config.EnableLaunchInfo,
                folderPolicyString = GetFolderPolicy(config),
                nativeLogFile
            },
            info = new
            {
                versionType = config.Type == BedrockInstanceReleaseType.Release ? 1 : 0
            }
        };

        var configPath = Path.Combine(configFolder, "config.json");
        Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrock_writingDataIsolationConfig.CurrentValue(), configPath));
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
                throw new InvalidDataException(CommonLanguageManager.Instance.bedrockLaunch_cannotUpdatePreloadImport.CurrentValue());

            var originalNameLength = currentDllName.Length;
            if (preloadDllName.Length > originalNameLength)
                throw new InvalidOperationException(
                    string.Format(CommonLanguageManager.Instance.bedrockLaunch_preloadImportNameTooLong.CurrentValue(), preloadDllName));

            var nameBuffer = new byte[originalNameLength + 1];
            var nameBytes = System.Text.Encoding.ASCII.GetBytes(preloadDllName);
            Array.Copy(nameBytes, nameBuffer, nameBytes.Length);
            peFile.RawFile.WriteBytes(descriptor.Name.RvaToOffset(peFile.ImageSectionHeaders!), nameBuffer);
            peFile.Flush();
        }
        catch (Exception exception) when (exception is not InvalidOperationException and not InvalidDataException)
        {
            throw new InvalidOperationException(
                string.Format(CommonLanguageManager.Instance.bedrockLaunch_cannotPatchImportTable.CurrentValue(), preloadDllName), exception);
        }
    }

    private static void AddBootstrapImport(string gameExecutable)
    {
        try
        {
            using var stream = new FileStream(gameExecutable, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            var peFile = new PeFile(stream);
            if (peFile.ImportedFunctions?.Any(import =>
                    string.Equals(import.DLL, BootstrapDllName, StringComparison.OrdinalIgnoreCase)) == true)
                return;
            peFile.AddImport(BootstrapDllName, "Load");
            peFile.Flush();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Cannot add {BootstrapDllName} to the Minecraft import table.", exception);
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

        throw new IOException(CommonLanguageManager.Instance.bedrockLaunch_cannotCreateFallbackFile.CurrentValue());
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
                
                Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrock_deleteStaleIsolationComponentFailed.CurrentValue(), path, Environment.NewLine, exception));
            }
            catch (UnauthorizedAccessException exception)
            {
                
                Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrock_deleteStaleIsolationComponentDenied.CurrentValue(), path, Environment.NewLine, exception));
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
                log?.Invoke(string.Format(LogLanguageManager.Instance.bedrock_deleteStalePreloadComponentFailed.CurrentValue(), stalePath, exception), BedrockLogLevel.Warning);
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
            log?.Invoke(string.Format(LogLanguageManager.Instance.bedrock_deleteStaleXboxHookFailed.CurrentValue(), staleHookPath, exception), BedrockLogLevel.Warning);
        }
    }
}
