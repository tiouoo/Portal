using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Media;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Pages;

public partial class DebugPage : DataUserControl, ITioTabPage
{
    public DebugPage()
    {
        InitializeComponent();
        DataContext = this;
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = "调试",
        Icon = StreamGeometry.Parse(
            "F1 M640,640z M0,0z M224,160C224,107 267,64 320,64 373,64 416,107 416,160L416,163.6C416,179.3,403.3,192,387.6,192L252.5,192C236.8,192,224.1,179.3,224.1,163.6L224.1,160z M569.6,172.8C580.2,186.9,577.3,207,563.2,217.6L465.4,290.9C470.7,299.8,474.7,309.6,477.2,320L576,320C593.7,320 608,334.3 608,352 608,369.7 593.7,384 576,384L480,384 480,416C480,418.6,479.9,421.3,479.8,423.9L563.2,486.4C577.3,497 580.2,517.1 569.6,531.2 559,545.3 538.9,548.2 524.8,537.6L461.7,490.3C438.5,534.5,395.2,566.5,344,574.2L344,344C344,330.7 333.3,320 320,320 306.7,320 296,330.7 296,344L296,574.2C244.8,566.5,201.5,534.5,178.3,490.3L115.2,537.6C101.1,548.2 81,545.3 70.4,531.2 59.8,517.1 62.7,497 76.8,486.4L160.2,423.9C160.1,421.3,160,418.7,160,416L160,384 64,384C46.3,384 32,369.7 32,352 32,334.3 46.3,320 64,320L162.8,320C165.3,309.6,169.3,299.8,174.6,290.9L76.8,217.6C62.7,207 59.8,186.9 70.4,172.8 81,158.7 101.1,155.8 115.2,166.4L224,248C236.3,242.9,249.8,240,264,240L376,240C390.2,240,403.7,242.8,416,248L524.8,166.4C538.9,155.8,559,158.7,569.6,172.8z")
    };

    public TabEntry HostTab { get; set; }

    private void Click1(object? sender, RoutedEventArgs e)
    {
        var a = 0;
        // ReSharper disable once IntDivisionByZero
        _ = 1 / a;
    }

    private async void StartWorkflowTest(object? sender, RoutedEventArgs e)
    {
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = "测试：下载整合包",
            Description = "正在准备下载任务",
            Progress = 0
        });
        var manifest = task.CreateChild(new TaskOptions
        {
            Name = "获取版本清单",
            Description = "等待下载开始",
            Progress = 0
        }, RunManifestAsync);
        var download = task.CreateChild(new TaskOptions
        {
            Name = "下载游戏文件",
            Description = "等待清单完成",
            Progress = 0
        }, RunDownloadAsync);
        var verify = task.CreateChild(new TaskOptions
        {
            Name = "校验下载内容",
            Description = "等待文件下载完成",
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
            Name = "测试：等待网络",
            Description = "正在等待网络连接"
        }, async context =>
        {
            context.SetWaiting("正在等待测试网络恢复");
            await Task.Delay(TimeSpan.FromSeconds(8), context.CancellationToken);
            context.SetRunning("网络已恢复，正在完成任务");
            context.ReportProgress(1);
        });
        task.Start();
    }

    private async void StartNestedWorkflowTest(object? sender, RoutedEventArgs e)
    {
        var root = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = "测试：嵌套安装流程",
            Description = "正在构建多层任务树",
            Progress = 0
        });
        var prepare = root.CreateChild(new TaskOptions
        {
            Name = "准备安装环境",
            Description = "等待执行",
            Progress = 0
        }, context => RunStepsAsync(context, "准备环境", 3));
        var downloadGroup = root.CreateChild(new TaskOptions
        {
            Name = "下载资源组",
            Description = "包含客户端和资源文件",
            Progress = 0
        });
        var client = downloadGroup.CreateChild(new TaskOptions
        {
            Name = "下载客户端",
            Description = "等待资源组开始",
            Progress = 0
        }, context => RunStepsAsync(context, "下载客户端", 4));
        var assetsGroup = downloadGroup.CreateChild(new TaskOptions
        {
            Name = "下载资源文件",
            Description = "包含索引与对象文件",
            Progress = 0
        });
        var index = assetsGroup.CreateChild(new TaskOptions
        {
            Name = "下载资源索引",
            Description = "等待资源文件阶段",
            Progress = 0
        }, context => RunStepsAsync(context, "下载资源索引", 3));
        var objects = assetsGroup.CreateChild(new TaskOptions
        {
            Name = "下载资源对象",
            Description = "等待资源索引完成",
            Progress = 0
        }, context => RunStepsAsync(context, "下载资源对象", 5));
        var verify = root.CreateChild(new TaskOptions
        {
            Name = "校验安装结果",
            Description = "等待下载完成",
            Progress = 0
        }, context => RunStepsAsync(context, "校验文件", 3));

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
            Name = "测试：下载失败",
            Description = "将在短暂执行后模拟失败"
        }, async context =>
        {
            context.SetRunning("正在请求不可用的测试资源");
            context.ReportProgress(0.4);
            await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken);
            throw new InvalidOperationException("测试用下载资源不可用。");
        });
        task.Start();
    }

    private void StartCancellableTest(object? sender, RoutedEventArgs e)
    {
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = "测试：可取消的长任务",
            Description = "可在任务卡片中点击“取消任务”"
        }, async context =>
        {
            for (var step = 1; step <= 30; step++)
            {
                context.SetRunning($"正在执行第 {step}/30 个步骤");
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
            Name = "测试：带操作按钮的任务",
            Description = "点击下方“模拟重试”验证任务操作",
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = "模拟重试",
                    Description = "等待一秒并写入操作日志。",
                    ExecuteAsync = async (managedTask, cancellationToken) =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                        // 操作日志可在任务导出的格式化日志中确认。
                    }
                }
            ]
        });
        task.Start();
    }

    private static async Task RunManifestAsync(TaskExecutionContext context)
    {
        context.SetWaiting("正在等待版本服务响应");
        await Task.Delay(TimeSpan.FromSeconds(1), context.CancellationToken);
        context.SetRunning("正在下载版本清单");
        for (var step = 1; step <= 4; step++)
        {
            context.ReportProgress(step / 4d);
            await Task.Delay(TimeSpan.FromMilliseconds(350), context.CancellationToken);
        }
    }

    private static async Task RunDownloadAsync(TaskExecutionContext context)
    {
        context.SetRunning("正在下载 client.jar");
        for (var step = 1; step <= 10; step++)
        {
            context.SetDescription($"正在下载文件 {step}/10");
            context.ReportProgress(step / 10d);
            await Task.Delay(TimeSpan.FromMilliseconds(350), context.CancellationToken);
        }
    }

    private static async Task RunVerifyAsync(TaskExecutionContext context)
    {
        context.SetRunning("正在校验文件哈希");
        for (var step = 1; step <= 5; step++)
        {
            context.ReportProgress(step / 5d);
            await Task.Delay(TimeSpan.FromMilliseconds(350), context.CancellationToken);
        }
    }

    private static async Task RunStepsAsync(TaskExecutionContext context, string action, int steps)
    {
        context.SetRunning($"正在{action} 0/{steps}");
        for (var step = 1; step <= steps; step++)
        {
            context.SetDescription($"正在{action} {step}/{steps}");
            context.ReportProgress(step / (double)steps);
            await Task.Delay(TimeSpan.FromMilliseconds(350), context.CancellationToken);
        }
    }
}
