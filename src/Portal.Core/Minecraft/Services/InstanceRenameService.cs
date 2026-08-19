using System.Text.Json.Nodes;
using Portal.Core.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;

namespace Portal.Core.Minecraft.Services;

public static class InstanceRenameService
{
    public static ManagedTask CreateRenameTask(MinecraftInstance instance, string newId)
    {
        var trimmedId = newId.Trim();
        return TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = string.Format(CommonLanguageManager.Instance.instanceRename_taskName.CurrentValue(), instance.InstanceName),
            Description = CommonLanguageManager.Instance.instanceRename_preparing.CurrentValue(),
            Progress = 0,
            Actions = []
        }, context => RunRenameAsync(context, instance, trimmedId));
    }

    private static async Task RunRenameAsync(TaskExecutionContext context, MinecraftInstance instance, string newId)
    {
        if (instance.Type != MinecraftInstanceType.Java || instance.MinecraftEntry is not { } minecraftEntry)
            throw new InvalidOperationException(CommonLanguageManager.Instance.instanceRename_onlyJava.CurrentValue());

        if (!instance.CanModifyVersion || instance.Layout is not { } layout)
            throw new InvalidOperationException(CommonLanguageManager.Instance.instanceRename_onlyPortal.CurrentValue());

        var oldId = minecraftEntry.Id;
        if (string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(CommonLanguageManager.Instance.instanceRename_sameName.CurrentValue());

        var instancesRoot = Path.GetDirectoryName(layout.InstanceRoot)
                            ?? throw new InvalidOperationException(CommonLanguageManager.Instance.instanceRename_cannotLocateDirectory.CurrentValue());
        var newInstanceDirectory = Path.Combine(instancesRoot, newId);
        if (Directory.Exists(newInstanceDirectory))
            throw new InvalidOperationException(string.Format(CommonLanguageManager.Instance.instanceRename_idExists.CurrentValue(), newId));

        var oldInstanceDirectory = layout.InstanceRoot;
        var metadataRoot = layout.MetadataRoot;
        var oldNativesDirectory = Path.Combine(metadataRoot, "natives", oldId);
        var newNativesDirectory = Path.Combine(metadataRoot, "natives", newId);

        var oldConfigPath = MinecraftInstance.GetExternalConfigPath(layout.Kind, oldInstanceDirectory);
        var newConfigPath = MinecraftInstance.GetExternalConfigPath(layout.Kind, newInstanceDirectory);
        var oldIconPath = oldConfigPath + ".png";
        var newIconPath = newConfigPath + ".png";
        var oldIconDirectory = Path.Combine(Path.GetDirectoryName(oldConfigPath)!,
            Path.GetFileNameWithoutExtension(oldConfigPath));
        var newIconDirectory = Path.Combine(Path.GetDirectoryName(newConfigPath)!,
            Path.GetFileNameWithoutExtension(newConfigPath));

        var undoActions = new List<Action>();
        try
        {
            await MinecraftInstallationTasks.RunStepAsync(context, CommonLanguageManager.Instance.instanceRename_migrateSettingsStep.CurrentValue(),
                CommonLanguageManager.Instance.instanceRename_migratingSettings.CurrentValue(), step =>
            {
                if (MoveFileIfExists(oldConfigPath, newConfigPath))
                    undoActions.Add(() => MoveFileIfExists(newConfigPath, oldConfigPath));
                if (MoveFileIfExists(oldIconPath, newIconPath))
                    undoActions.Add(() => MoveFileIfExists(newIconPath, oldIconPath));
                if (MoveDirectoryIfExists(oldIconDirectory, newIconDirectory))
                    undoActions.Add(() => MoveDirectoryIfExists(newIconDirectory, oldIconDirectory));
                step.ReportProgress(1);
                return Task.CompletedTask;
            });

            await MinecraftInstallationTasks.RunStepAsync(context, CommonLanguageManager.Instance.instanceRename_renameVersionFilesStep.CurrentValue(),
                CommonLanguageManager.Instance.instanceRename_renamingVersionFiles.CurrentValue(), step =>
            {
                var oldJson = Path.Combine(oldInstanceDirectory, $"{oldId}.json");
                var newJson = Path.Combine(oldInstanceDirectory, $"{newId}.json");
                if (File.Exists(oldJson))
                {
                    File.Move(oldJson, newJson);
                    RewriteJsonId(newJson, newId);
                    undoActions.Add(() =>
                    {
                        if (File.Exists(newJson))
                            File.Move(newJson, oldJson);
                        RewriteJsonId(oldJson, oldId);
                    });
                }

                var oldJar = Path.Combine(oldInstanceDirectory, $"{oldId}.jar");
                var newJar = Path.Combine(oldInstanceDirectory, $"{newId}.jar");
                if (File.Exists(oldJar))
                {
                    File.Move(oldJar, newJar);
                    undoActions.Add(() =>
                    {
                        if (File.Exists(newJar))
                            File.Move(newJar, oldJar);
                    });
                }

                step.ReportProgress(1);
                return Task.CompletedTask;
            });

            await MinecraftInstallationTasks.RunStepAsync(context, CommonLanguageManager.Instance.instanceRename_renameDirectoryStep.CurrentValue(),
                string.Format(CommonLanguageManager.Instance.instanceRename_renamingDirectory.CurrentValue(), newId), step =>
                {
                    Directory.Move(oldInstanceDirectory, newInstanceDirectory);
                    undoActions.Add(() => Directory.Move(newInstanceDirectory, oldInstanceDirectory));
                    step.ReportProgress(1);
                    return Task.CompletedTask;
                });

            await MinecraftInstallationTasks.RunStepAsync(context, CommonLanguageManager.Instance.instanceRename_migrateNativesStep.CurrentValue(),
                CommonLanguageManager.Instance.instanceRename_migratingNatives.CurrentValue(), step =>
            {
                if (MoveDirectoryIfExists(oldNativesDirectory, newNativesDirectory))
                    undoActions.Add(() => MoveDirectoryIfExists(newNativesDirectory, oldNativesDirectory));
                step.ReportProgress(1);
                return Task.CompletedTask;
            });

            await RefreshInstancesAsync(context);
            context.SetDescription(string.Format(CommonLanguageManager.Instance.instanceRename_complete.CurrentValue(), instance.InstanceName, newId));
        }
        catch (Exception exception)
        {
            Logger.Error(string.Format(LogLanguageManager.Instance.instanceRename_failedRollingBack.CurrentValue(), instance.InstanceName, Environment.NewLine, exception));
            for (var i = undoActions.Count - 1; i >= 0; i--)
                try
                {
                    undoActions[i]();
                }
                catch (Exception rollbackException)
                {
                    Logger.Warning(string.Format(LogLanguageManager.Instance.instanceRename_rollbackFailed.CurrentValue(), rollbackException));
                }

            throw;
        }
    }

    private static void RewriteJsonId(string jsonPath, string id)
    {
        try
        {
            if (!File.Exists(jsonPath)) return;
            if (JsonNode.Parse(File.ReadAllText(jsonPath)) is not JsonObject node) return;
            node["id"] = id;
            File.WriteAllText(jsonPath, node.ToJsonString());
        }
        catch (Exception exception)
        {
            Logger.Warning($"[InstanceRename] Failed to rewrite instance id in {jsonPath}: {exception}");
        }
    }

    private static bool MoveFileIfExists(string source, string destination)
    {
        if (!File.Exists(source)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination, true);
        return true;
    }

    private static bool MoveDirectoryIfExists(string source, string destination)
    {
        if (!Directory.Exists(source)) return false;
        if (Directory.Exists(destination))
            DeleteDirectory(destination);
        Directory.Move(source, destination);
        return true;
    }

    private static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[InstanceRename] Failed to clean up {directory}: {exception}");
        }
    }

    private static async Task RefreshInstancesAsync(TaskExecutionContext context)
    {
        await MinecraftInstallationTasks.RunStepAsync(context, CommonLanguageManager.Instance.instanceRename_refreshInstancesStep.CurrentValue(),
            CommonLanguageManager.Instance.instanceRename_scanningInstances.CurrentValue(), step =>
        {
            InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
            step.SetDescription(CommonLanguageManager.Instance.instanceRename_refreshingInstances.CurrentValue());
            step.ReportProgress(1);
            return Task.CompletedTask;
        });
    }
}