using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Graphics;
using Portal.Core.Minecraft.Instance.Bedrock;
using Portal.Localization;
using Portal.ViewModels;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.InstancePages;

public partial class Properties : Dsc, INotifyPropertyChanged
{
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

    public MinecraftInstance Instance { get; }
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsUwpBedrock => Instance?.BedrockConfig?.BuildType == BedrockBuildType.UWP;
    public bool IsGdkBedrock => Instance?.BedrockConfig?.BuildType == BedrockBuildType.GDK;
    public bool SupportsBedrockDataIsolation => Instance?.IsBedrock == true && !IsUwpBedrock;
    public bool IsBedrock => Instance?.IsBedrock == true;

    public bool EnableLaunchInfo
    {
        get => Instance?.BedrockConfig?.EnableLaunchInfo ?? true;
        set => UpdateBedrockConfig(config => config.EnableLaunchInfo = value);
    }

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

    private string InstanceVersionId
    {
        get
        {
            var entry = Instance?.MinecraftEntry;
            if (entry is null)
                return string.Empty;
            return entry.MinecraftVersion;
        }
    }

    public bool CanChooseGraphicsBackend
    {
        get
        {
            if (Instance?.JavaConfig == null)
                return false;
            return GameVersion.Parse(InstanceVersionId).CanChooseBackend();
        }
    }

    public bool CanChooseVulkan
    {
        get
        {
            if (Instance?.JavaConfig == null)
                return false;
            return GraphicsApi.Vulkan.IsSupported(GameVersion.Parse(InstanceVersionId));
        }
    }

    public IReadOnlyList<GraphicsApiOption> GraphicsApiOptions { get; } = new[]
    {
        new GraphicsApiOption(GraphicsApi.Default, CommonLanguageManager.Instance.renderer_default.CurrentValue()),
        new GraphicsApiOption(GraphicsApi.OpenGL, "OpenGL"),
        new GraphicsApiOption(GraphicsApi.Vulkan, "Vulkan")
    };

    public GraphicsApiOption? SelectedGraphicsApi
    {
        get => GraphicsApiOptions.FirstOrDefault(option => Equals(option.Value, CurrentGraphicsApi));
        set
        {
            if (Instance?.JavaConfig == null || value is null)
                return;
            if (Instance.JavaConfig.GraphicsBackend != value.Value)
            {
                Instance.JavaConfig.GraphicsBackend = value.Value;
                Instance.SaveConfig();
                OnPropertyChanged();
            }
        }
    }

    private GraphicsApi CurrentGraphicsApi =>
        Instance?.JavaConfig?.GraphicsBackend ?? GraphicsApi.Default;

    public IReadOnlyList<Renderer> OpenGlRendererOptions =>
        Renderers.GetOpenGlRenderers();

    public IReadOnlyList<Renderer> VulkanRendererOptions =>
        Renderers.GetVulkanRenderers();

    public Renderer SelectedOpenGlRenderer
    {
        get => Renderers.Resolve(Instance?.JavaConfig?.OpenGlRenderer);
        set
        {
            if (Instance?.JavaConfig == null)
                return;
            if (!string.Equals(Instance.JavaConfig.OpenGlRenderer, value.Name, StringComparison.OrdinalIgnoreCase))
            {
                Instance.JavaConfig.OpenGlRenderer = value.Name;
                Instance.SaveConfig();
                OnPropertyChanged();
            }
        }
    }

    public Renderer SelectedVulkanRenderer
    {
        get => Renderers.Resolve(Instance?.JavaConfig?.VulkanRenderer);
        set
        {
            if (Instance?.JavaConfig == null)
                return;
            if (!string.Equals(Instance.JavaConfig.VulkanRenderer, value.Name, StringComparison.OrdinalIgnoreCase))
            {
                Instance.JavaConfig.VulkanRenderer = value.Name;
                Instance.SaveConfig();
                OnPropertyChanged();
            }
        }
    }

    public bool IsVulkanRendererVisible => CanChooseVulkan;

    public bool IsOpenGlRendererVisible => true;

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        Instance.SaveConfig();
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
            _ = (sender as Control)!.GetTopLevel().Launcher
                .LaunchDirectoryInfoAsync(new DirectoryInfo(Instance.InstanceFolderPath));
    }

    private void UpdateBedrockConfig(Action<BedrockInstanceConfig> update)
    {
        if (Instance?.BedrockConfig == null)
            return;

        update(Instance.BedrockConfig);
        BedrockHelper.SaveInstanceConfig(Instance.BedrockConfig);
    }

    public sealed record GraphicsApiOption(GraphicsApi Value, string DisplayName)
    {
        public override string ToString()
        {
            return DisplayName;
        }
    }
}
