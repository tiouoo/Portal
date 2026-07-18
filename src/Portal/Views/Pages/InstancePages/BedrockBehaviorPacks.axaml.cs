using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Portal.Core.Minecraft.Classes;

namespace Portal.Views.Pages.InstancePages;

public partial class BedrockBehaviorPacks : UserControl
{
    private readonly MinecraftInstance? _instance;

    public BedrockBehaviorPacks()
    {
        InitializeComponent();
    }

    public BedrockBehaviorPacks(MinecraftInstance instance)
    {
        InitializeComponent();
        _instance = instance;
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel || _instance == null)
            return;

        await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(_instance.GetSpecialFolder(MinecraftSpecialFolder.BehaviorPacksFolder)));
    }

    private void Title_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
    }
}
