using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Portal.Core.Minecraft.Classes;

namespace Portal.Views.Pages.InstancePages;

public partial class BedrockSkins : UserControl
{
    private readonly MinecraftInstance? _instance;

    public BedrockSkins()
    {
        InitializeComponent();
    }

    public BedrockSkins(MinecraftInstance instance)
    {
        InitializeComponent();
        _instance = instance;
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel || _instance == null)
            return;

        await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(_instance.GetSpecialFolder(MinecraftSpecialFolder.SkinPacksFolder)));
    }

    private void Title_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
    }
}
