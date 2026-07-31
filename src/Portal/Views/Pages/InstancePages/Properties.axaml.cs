using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance.Bedrock;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Extensions;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.InstancePages;

public partial class Properties : DataUserControl
{
    public MinecraftInstance Instance { get; }
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsUwpBedrock => Instance?.BedrockConfig?.BuildType == Bedrock.Standard.Manifest.BedrockBuildType.UWP;
    public bool IsGdkBedrock => Instance?.BedrockConfig?.BuildType == Bedrock.Standard.Manifest.BedrockBuildType.GDK;
    public bool SupportsBedrockDataIsolation => Instance?.IsBedrock == true && !IsUwpBedrock;

    public bool EnableMouseLock
    {
        get => Instance?.BedrockConfig?.EnableMouseLock ?? false;
        set => UpdateBedrockConfig(config => config.EnableMouseLock = value);
    }

    public bool EnableMouseLockForGdk
    {
        get => Instance?.BedrockConfig?.EnableMouseLockForGdk ?? false;
        set => UpdateBedrockConfig(config => config.EnableMouseLockForGdk = value);
    }

    public int MouseLockInset
    {
        get => Instance?.BedrockConfig?.MouseLockInset ?? 2;
        set => UpdateBedrockConfig(config => config.MouseLockInset = Math.Clamp(value, 0, 100));
    }

    public string MouseLockHotkey
    {
        get => Instance?.BedrockConfig?.MouseLockHotkey ?? "Ctrl+Alt";
        set => UpdateBedrockConfig(config => config.MouseLockHotkey =
            string.IsNullOrWhiteSpace(value) ? "Ctrl+Alt" : value.Trim());
    }

    public string BedrockLaunchArguments
    {
        get => Instance?.BedrockConfig?.LaunchArguments ?? string.Empty;
        set => UpdateBedrockConfig(config => config.LaunchArguments = value ?? string.Empty);
    }

    public bool EnableCreatorEditor
    {
        get => Instance?.BedrockConfig?.EnableCreatorEditor ?? false;
        set => UpdateBedrockConfig(config => config.EnableCreatorEditor = value);
    }

    public Properties(MinecraftInstance instance)
    {
        Instance = instance;
        InitializeComponent();
        DataContext = this;
    }
    public Properties()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void Save_Click(object? sender, RoutedEventArgs e) => Instance.SaveConfig();

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
            _ = (sender as Control)!.GetTopLevel().Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(Instance.InstanceFolderPath));
    }

    private void UpdateBedrockConfig(Action<Bedrock.Standard.Manifest.BedrockInstanceConfig> update)
    {
        if (Instance?.BedrockConfig == null)
            return;

        update(Instance.BedrockConfig);
        BedrockHelper.SaveInstanceConfig(Instance.BedrockConfig);
    }
}
