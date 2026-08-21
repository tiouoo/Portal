using Avalonia.Interactivity;
using Avalonia.Media;
using Portal.Localization;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

using Portal.Module;
namespace Portal.Views.Pages;

public partial class DebugPage : Dsc, ITioTabPage
{
    public DebugPage()
    {
        InitializeComponent();
        DataContext = this;
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = CommonLanguageManager.Instance.debug_pageTitle.CurrentValue(),
        Icon = GeometryResources.Get("DebugGeometry")
    };

    public TabEntry HostTab { get; set; }

    private void Click1(object? sender, RoutedEventArgs e)
    {
        var a = 0;

        _ = 1 / a;
    }

    private async void StartWorkflowTest(object? sender, RoutedEventArgs e)
    {
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.debug_testDownloadModpack.CurrentValue(),
            Description = CommonLanguageManager.Instance.debug_preparingDownloadTask.CurrentValue(),
            Progress = 0
        });
        var manifest = task.CreateChild(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.debug_fetchVersionManifest.CurrentValue(),
            Description = CommonLanguageManager.Instance.debug_waitingDownloadStart.CurrentValue(),
            Progress = 0
        }, RunManifestAsync);
        var download = task.CreateChild(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.debug_downloadGameFiles.CurrentValue(),
            Description = CommonLanguageManager.Instance.debug_waitingManifestComplete.CurrentValue(),
            Progress = 0
        }, RunDownloadAsync);
        var verify = task.CreateChild(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.debug_verifyDownload.CurrentValue(),
            Description = CommonLanguageManager.Instance.debug_waitingFilesDownload.CurrentValue(),
            Progress = 0
        }, RunVerifyAsync);

        task.Start();
        manifest.Start();
        await manifest.Completion;
        download.Start();
        await download.Completion;
        verify.Start();
        await verify.Completion;
        task.Complete();
    }

    private void StartWaitingTest(object? sender, RoutedEventArgs e)
    {
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.debug_testWaitNetwork.CurrentValue(),
            Description = CommonLanguageManager.Instance.debug_waitingNetwork.CurrentValue()
        }, async context =>
        {
            context.SetWaiting(CommonLanguageManager.Instance.debug_waitingNetworkRecovery.CurrentValue());
            await Task.Delay(TimeSpan.FromSeconds(8), context.CancellationToken);
            context.SetRunning(CommonLanguageManager.Instance.debug_networkRecovered.CurrentValue());
            context.ReportProgress(1);
        });
        task.Start();
    }

    private async void StartNestedWorkflowTest(object? sender, RoutedEventArgs e)
    {
        var root = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.debug_testNestedInstall.CurrentValue(),
            Description = CommonLanguageManager.Instance.debug_buildingTaskTree.CurrentValue(),
            Progress = 0
        });
        var prepare = root.CreateChild(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.debug_prepareEnvironment.CurrentValue(),
            Description = CommonLanguageManager.Instance.debug_waitingExecute.CurrentValue(),
            Progress = 0
        }, context => RunStepsAsync(context, CommonLanguageManager.Instance.debug_prepareEnvironmentAction.CurrentValue(),
            3));
        var downloadGroup = root.CreateChild(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.debug_downloadResourceGroup.CurrentValue(),
            Description = CommonLanguageManager.Instance.debug_resourceGroupDescription.CurrentValue(),
            Progress = 0
        });
        var client = downloadGroup.CreateChild(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.debug_downloadClient.CurrentValue(),
            Description = CommonLanguageManager.Instance.debug_waitingResourceGroupStart.CurrentValue(),
            Progress = 0
        }, context => RunStepsAsync(context, CommonLanguageManager.Instance.debug_downloadClientAction.CurrentValue(), 4));
        var assetsGroup = downloadGroup.CreateChild(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.debug_downloadResourceFiles.CurrentValue(),
            Description = CommonLanguageManager.Instance.debug_resourceFilesDescription.CurrentValue(),
            Progress = 0
        });
        var index = assetsGroup.CreateChild(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.debug_downloadResourceIndex.CurrentValue(),
            Description = CommonLanguageManager.Instance.debug_waitingResourceFilesStage.CurrentValue(),
            Progress = 0
        }, context => RunStepsAsync(context, CommonLanguageManager.Instance.debug_downloadResourceIndexAction.CurrentValue(),
            3));
        var objects = assetsGroup.CreateChild(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.debug_downloadResourceObjects.CurrentValue(),
            Description = CommonLanguageManager.Instance.debug_waitingResourceIndexComplete.CurrentValue(),
            Progress = 0
        }, context => RunStepsAsync(context, CommonLanguageManager.Instance.debug_downloadResourceObjectsAction.CurrentValue(),
            5));
        var verify = root.CreateChild(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.debug_verifyInstall.CurrentValue(),
            Description = CommonLanguageManager.Instance.debug_waitingDownloadComplete.CurrentValue(),
            Progress = 0
        }, context => RunStepsAsync(context, CommonLanguageManager.Instance.debug_verifyFilesAction.CurrentValue(), 3));

        root.Start();
        prepare.Start();
        await prepare.Completion;
        downloadGroup.Start();
        client.Start();
        await client.Completion;
        assetsGroup.Start();
        index.Start();
        await index.Completion;
        objects.Start();
        await objects.Completion;
        assetsGroup.Complete();
        downloadGroup.Complete();
        verify.Start();
        await verify.Completion;
        root.Complete();
    }

    private void StartFaultedTest(object? sender, RoutedEventArgs e)
    {
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.debug_testDownloadFailed.CurrentValue(),
            Description = CommonLanguageManager.Instance.debug_simulateFailure.CurrentValue()
        }, async context =>
        {
            context.SetRunning(CommonLanguageManager.Instance.debug_requestingUnavailableResource.CurrentValue());
            context.ReportProgress(0.4);
            await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken);
            throw new InvalidOperationException(CommonLanguageManager.Instance.debug_testResourceUnavailable.CurrentValue());
        });
        task.Start();
    }

    private void StartCancellableTest(object? sender, RoutedEventArgs e)
    {
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.debug_testCancellableTask.CurrentValue(),
            Description = CommonLanguageManager.Instance.debug_cancellableDescription.CurrentValue()
        }, async context =>
        {
            for (var step = 1; step <= 30; step++)
            {
                context.SetRunning(string.Format(CommonLanguageManager.Instance.debug_executingStep.CurrentValue(),
                    step));
                context.ReportProgress(step / 30d);
                await Task.Delay(TimeSpan.FromSeconds(1), context.CancellationToken);
            }
        });
        task.Start();
    }

    private void StartActionTest(object? sender, RoutedEventArgs e)
    {
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.debug_testActionTask.CurrentValue(),
            Description = CommonLanguageManager.Instance.debug_actionTaskDescription.CurrentValue(),
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = CommonLanguageManager.Instance.debug_simulateRetry.CurrentValue(),
                    Description = CommonLanguageManager.Instance.debug_simulateRetryDescription.CurrentValue(),
                    ExecuteAsync = async (managedTask, cancellationToken) =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                }
            ]
        });
        task.Start();
    }

    private static async Task RunManifestAsync(TaskExecutionContext context)
    {
        context.SetWaiting(CommonLanguageManager.Instance.debug_waitingVersionService.CurrentValue());
        await Task.Delay(TimeSpan.FromSeconds(1), context.CancellationToken);
        context.SetRunning(CommonLanguageManager.Instance.debug_downloadingVersionManifest.CurrentValue());
        for (var step = 1; step <= 4; step++)
        {
            context.ReportProgress(step / 4d);
            await Task.Delay(TimeSpan.FromMilliseconds(350), context.CancellationToken);
        }
    }

    private static async Task RunDownloadAsync(TaskExecutionContext context)
    {
        context.SetRunning(CommonLanguageManager.Instance.debug_downloadingClientJar.CurrentValue());
        for (var step = 1; step <= 10; step++)
        {
            context.SetDescription(string.Format(CommonLanguageManager.Instance.debug_downloadingFile.CurrentValue(),
                step));
            context.ReportProgress(step / 10d);
            await Task.Delay(TimeSpan.FromMilliseconds(350), context.CancellationToken);
        }
    }

    private static async Task RunVerifyAsync(TaskExecutionContext context)
    {
        context.SetRunning(CommonLanguageManager.Instance.debug_verifyingFileHashes.CurrentValue());
        for (var step = 1; step <= 5; step++)
        {
            context.ReportProgress(step / 5d);
            await Task.Delay(TimeSpan.FromMilliseconds(350), context.CancellationToken);
        }
    }

    private static async Task RunStepsAsync(TaskExecutionContext context, string action, int steps)
    {
        context.SetRunning(string.Format(CommonLanguageManager.Instance.debug_runningAction.CurrentValue(), action, 0,
            steps));
        for (var step = 1; step <= steps; step++)
        {
            context.SetDescription(string.Format(CommonLanguageManager.Instance.debug_runningActionStep.CurrentValue(),
                action, step, steps));
            context.ReportProgress(step / (double)steps);
            await Task.Delay(TimeSpan.FromMilliseconds(350), context.CancellationToken);
        }
    }
}