using System.Text.Json.Nodes;
using MinecraftLaunch.Base.Models.Game;
using Portal.Const;
using Portal.Core.Helpers;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Views.Pages.DownloadPages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;

namespace Portal.Services;

/// <summary>
/// 重命名 Portal MC 实例：重命名实例 ID（实例目录与版本文件 json/jar 同步重命名），
/// 并迁移以路径或 ID 为键的实例配置（版本设置、备注、收藏、游玩统计）、自定义图标
/// 与共享 meta 下的原生库目录。任一步失败时回滚全部已完成的迁移。
/// </summary>
public static class InstanceRenameService
{
    public static ManagedTask CreateRenameTask(MinecraftInstance instance, string newId)
    {
        var trimmedId = newId.Trim();
        return TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = $"重命名实例 {instance.InstanceName}",
            Description = "正在准备重命名实例",
            Progress = 0,
            Actions = []
        }, context => RunRenameAsync(context, instance, trimmedId));
    }

    private static async Task RunRenameAsync(TaskExecutionContext context, MinecraftInstance instance, string newId)
    {
        if (instance.Type != MinecraftInstanceType.Java || instance.MinecraftEntry is not { } minecraftEntry)
            throw new InvalidOperationException("仅支持重命名 Java 版实例。");

        if (!instance.CanModifyVersion || instance.Layout is not { } layout)
            throw new InvalidOperationException("仅支持重命名 Portal 游戏目录下的实例，其他格式的实例请在对应启动器中重命名。");

        var oldId = minecraftEntry.Id;
        if (string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("新名称与原名称相同，无需重命名。");

        var instancesRoot = Path.GetDirectoryName(layout.InstanceRoot)
            ?? throw new InvalidOperationException("无法确定实例所在目录。");
        var newInstanceDirectory = Path.Combine(instancesRoot, newId);
        if (Directory.Exists(newInstanceDirectory))
            throw new InvalidOperationException($"实例 ID “{newId}”已存在，请更换名称。");

        var oldInstanceDirectory = layout.InstanceRoot;
        var metadataRoot = layout.MetadataRoot;
        var oldNativesDirectory = Path.Combine(metadataRoot, "natives", oldId);
        var newNativesDirectory = Path.Combine(metadataRoot, "natives", newId);

        var oldConfigPath = MinecraftInstance.GetExternalConfigPath(layout.Kind, oldInstanceDirectory);
        var newConfigPath = MinecraftInstance.GetExternalConfigPath(layout.Kind, newInstanceDirectory);
        var oldIconPath = Path.ChangeExtension(oldConfigPath, ".png");
        var newIconPath = Path.ChangeExtension(newConfigPath, ".png");
        var oldIconDirectory = Path.Combine(Path.GetDirectoryName(oldConfigPath)!,
            Path.GetFileNameWithoutExtension(oldConfigPath));
        var newIconDirectory = Path.Combine(Path.GetDirectoryName(newConfigPath)!,
            Path.GetFileNameWithoutExtension(newConfigPath));

        // 记录已执行操作，失败时逆序回滚：目录先移回原位置，再恢复其中的 json/jar 文件名与 id。
        var undoActions = new List<Action>();
        try
        {
            await MinecraftInstallationViewModel.RunStepAsync(context, "迁移实例设置", "正在迁移实例配置、图标与游玩记录", step =>
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

            await MinecraftInstallationViewModel.RunStepAsync(context, "重命名版本文件", "正在重命名实例版本文件", step =>
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

            await MinecraftInstallationViewModel.RunStepAsync(context, "重命名实例目录",
                $"正在将实例目录重命名为 {newId}", step =>
                {
                    Directory.Move(oldInstanceDirectory, newInstanceDirectory);
                    undoActions.Add(() => Directory.Move(newInstanceDirectory, oldInstanceDirectory));
                    step.ReportProgress(1);
                    return Task.CompletedTask;
                });

            await MinecraftInstallationViewModel.RunStepAsync(context, "迁移原生库目录", "正在迁移实例原生库目录", step =>
            {
                if (MoveDirectoryIfExists(oldNativesDirectory, newNativesDirectory))
                    undoActions.Add(() => MoveDirectoryIfExists(newNativesDirectory, oldNativesDirectory));
                step.ReportProgress(1);
                return Task.CompletedTask;
            });

            await RefreshInstancesAsync(context);
            context.SetDescription($"实例“{instance.InstanceName}”已重命名为 {newId}");
        }
        catch (Exception exception)
        {
            Logger.Error($"[InstanceRename] 重命名实例“{instance.InstanceName}”失败，正在回滚。{Environment.NewLine}{exception}");
            for (var i = undoActions.Count - 1; i >= 0; i--)
            {
                try
                {
                    undoActions[i]();
                }
                catch (Exception rollbackException)
                {
                    Logger.Warning($"[InstanceRename] 回滚失败：{rollbackException}");
                }
            }
            throw;
        }
    }

    /// <summary>将实例 json 的 id 字段改写为目标 id。</summary>
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

    private static async Task RefreshInstancesAsync(TaskExecutionContext context) =>
        await MinecraftInstallationViewModel.RunStepAsync(context, "刷新已安装实例", "正在扫描安装目录中的实例", step =>
        {
            InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
            step.SetDescription("正在刷新已安装实例");
            step.ReportProgress(1);
            return Task.CompletedTask;
        });
}
