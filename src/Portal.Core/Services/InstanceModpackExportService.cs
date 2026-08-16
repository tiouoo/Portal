using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Modpack;
using Tio.Avalonia.Standard.Modules.Tasks;

namespace Portal.Core.Services;

public static class InstanceModpackExportService
{
    public static ManagedTask CreateExportTask(MinecraftInstance instance, ModpackExportOptions options,
        string outputPath)
    {
        return TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = $"导出整合包 {options.PackName}",
            Description = "正在准备导出",
            Progress = 0,
            Actions = []
        }, context => RunExportAsync(context, instance, options, outputPath));
    }

    private static async Task RunExportAsync(TaskExecutionContext context, MinecraftInstance instance,
        ModpackExportOptions options, string outputPath)
    {
        if (instance.Type != MinecraftInstanceType.Java)
            throw new InvalidOperationException("仅支持导出 Java 版实例。");

        var lastStage = string.Empty;
        var lastProgress = double.NaN;
        await ModpackExportService.ExportAsync(instance, options, outputPath,
            report =>
            {
                if (report.Stage != lastStage)
                {
                    lastStage = report.Stage;
                    context.SetDescription(report.Description);
                }

                if (report.Progress > lastProgress)
                {
                    lastProgress = report.Progress;
                    context.ReportProgress(report.Progress);
                }
            });
    }
}