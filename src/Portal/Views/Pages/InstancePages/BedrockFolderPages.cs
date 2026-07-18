using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System.ComponentModel;
using Portal.Core.Minecraft.Classes;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Extensions;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.InstancePages;

public sealed class BedrockStorage(MinecraftInstance instance) : BedrockFolderPage(instance, "存储", "基岩版实例包与游戏数据目录", [
    ("游戏包", MinecraftSpecialFolder.InstanceFolder),
    ("世界", MinecraftSpecialFolder.SavesFolder),
    ("资源包", MinecraftSpecialFolder.ResourcePacksFolder),
    ("行为包", MinecraftSpecialFolder.BehaviorPacksFolder),
    ("皮肤包", MinecraftSpecialFolder.SkinPacksFolder),
    ("世界模板", MinecraftSpecialFolder.WorldTemplatesFolder),
    ("截图", MinecraftSpecialFolder.ScreenshotsFolder)
]);

public sealed class BedrockResourcePacks(MinecraftInstance instance) : BedrockFolderPage(instance, "资源包", "管理基岩版资源包目录", [
    ("资源包", MinecraftSpecialFolder.ResourcePacksFolder)
]);

public sealed class BedrockBehaviorPacks(MinecraftInstance instance) : BedrockFolderPage(instance, "行为包", "管理基岩版行为包目录", [
    ("行为包", MinecraftSpecialFolder.BehaviorPacksFolder)
]);

public sealed class BedrockWorlds(MinecraftInstance instance) : BedrockFolderPage(instance, "世界", "管理基岩版世界目录", [
    ("世界", MinecraftSpecialFolder.SavesFolder)
]);

public sealed class BedrockWorldTemplates(MinecraftInstance instance) : BedrockFolderPage(instance, "世界模板", "管理基岩版世界模板目录", [
    ("世界模板", MinecraftSpecialFolder.WorldTemplatesFolder)
]);

public sealed class BedrockSkins(MinecraftInstance instance) : BedrockFolderPage(instance, "皮肤包", "管理基岩版皮肤包目录", [
    ("皮肤包", MinecraftSpecialFolder.SkinPacksFolder)
]);

public class BedrockFolderPage : UserControl, IDisposable
{
    private readonly MinecraftInstance _instance;
    private readonly List<(MinecraftSpecialFolder Folder, TextBlock Path)> _folderPaths = [];
    private readonly TextBlock _scopeText;

    protected BedrockFolderPage(MinecraftInstance instance, string title, string description,
        IReadOnlyList<(string Name, MinecraftSpecialFolder Folder)> folders)
    {
        _instance = instance;
        var list = new StackPanel { Spacing = 8, Margin = new Thickness(10) };
        list.Children.Add(new TextBlock { Text = title, FontSize = 24 });
        list.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap
        });
        _scopeText = new TextBlock
        {
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 8)
        };
        list.Children.Add(_scopeText);

        foreach (var (name, folder) in folders)
        {
            var pathText = new TextBlock { FontSize = 12, Foreground = Brushes.Gray, TextTrimming = TextTrimming.CharacterEllipsis };
            var button = new Button
            {
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = name, FontSize = 16 },
                        pathText
                    }
                },
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12)
            };
            button.Click += OpenFolder;
            button.Tag = folder;
            list.Children.Add(button);
            _folderPaths.Add((folder, pathText));
        }

        Content = new ScrollViewer { Content = list };
        RefreshPaths();
        _ = instance.StorageUsage.EnsureLoadedAsync();
        instance.PropertyChanged += Instance_PropertyChanged;
    }

    private void Instance_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MinecraftInstance.EnableIndependentBedrockVersion) or
            nameof(MinecraftInstance.ShareBedrockDataWithOtherLaunchers))
        {
            RefreshPaths();
        }
    }

    private void RefreshPaths()
    {
        _scopeText.Text = _instance.UsesSharedBedrockData
            ? "当前实例与其他启动器共享基岩数据。删除或导入内容会影响使用该目录的其他启动器。"
            : _instance.EnableIndependentBedrockVersion
                ? "当前实例使用独立基岩数据。"
                : "当前实例使用 Portal 公共基岩数据。";
        foreach (var (folder, path) in _folderPaths)
            path.Text = _instance.GetSpecialFolder(folder);
    }

    private async void OpenFolder(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MinecraftSpecialFolder folder } || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var path = _instance.GetSpecialFolder(folder);
        await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }

    public void Dispose()
    {
        _instance.PropertyChanged -= Instance_PropertyChanged;
    }
}
