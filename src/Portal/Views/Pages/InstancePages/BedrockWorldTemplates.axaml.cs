using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Portal.Core.Minecraft.Classes;

namespace Portal.Views.Pages.InstancePages;

public partial class BedrockWorldTemplates : UserControl
{
    private readonly MinecraftInstance? _instance;
    private readonly MinecraftSpecialFolder _folder;

    public BedrockWorldTemplates()
    {
        InitializeComponent();
    }

    public BedrockWorldTemplates(MinecraftInstance instance)
    {
        InitializeComponent();
        _instance = instance;
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        if (_instance == null)
            return;

        await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(_instance.GetSpecialFolder(MinecraftSpecialFolder.WorldTemplatesFolder)));
    }

    private void Title_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        throw new NotImplementedException();
    }
}