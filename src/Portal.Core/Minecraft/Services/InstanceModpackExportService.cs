using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Modpack;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.Tasks;

namespace Portal.Core.Minecraft.Services;

public static class InstanceModpackExportService
{
    public static ManagedTask CreateExportTask(MinecraftInstance instance, ModpackExportOptions options,
        string outputPath)
    {
        return TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = string.Format(CommonLanguageManager.Instance.modpackExport_taskName.CurrentValue(), options.PackName),
            Description = CommonLanguageManager.Instance.modpackExport_preparing.CurrentValue(),
            Progress = 0,
            Actions = []
        }, context => RunExportAsync(context, instance, options, outputPath));
    }

    private static async Task RunExportAsync(TaskExecutionContext context, MinecraftInstance instance,
        ModpackExportOptions options, string outputPath)
    {
        if (instance.Type != MinecraftInstanceType.Java)
            throw new InvalidOperationException(CommonLanguageManager.Instance.modpackExport_onlyJava.CurrentValue());

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