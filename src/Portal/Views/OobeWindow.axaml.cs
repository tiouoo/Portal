using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Portal.Const;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Operations.Account;
using Portal.Core.Operations.Java;
using Portal.Core.Operations.OpenFile;
using Portal.Module.Animations;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Controls;

namespace Portal.Views;

public partial class OobeWindow : TioWindow
{
    private const int STEP_COUNT = 4;

    private static readonly SoftBackEaseOut DotsEasing = new() { Amplitude = 0.6 };

    private IntPtr _macOsWindowHandle;
    private int _dotsAnimationToken;

        public event Action? Completed;

    public Data Data => Data.Instance;

    public OobeWindow()
    {
        InitializeComponent();
        DataContext = this;

        Loaded += (_, _) => { ThemeListBox.SelectedIndex = (int)Data.ConfigEntry.Theme; };
        ThemeListBox.SelectionChanged += (_, _) =>
        {
            if (ThemeListBox.SelectedIndex == -1) return;
            Data.ConfigEntry.Theme = (TioUi.Shared.Theme)ThemeListBox.SelectedIndex;
        };

        GoToStep(0);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var nsWindow = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (nsWindow == IntPtr.Zero) return;
            _macOsWindowHandle = nsWindow;
            Loaded += (_, _) => RefreshMacOsTitleBarButtons(nsWindow);
            PropertyChanged += (_, args) =>
            {
                if (args.Property.Name != nameof(WindowState)) return;
                RefreshMacOsTitleBarButtons(nsWindow);
            };
            SizeChanged += (_, _) => RefreshMacOsTitleBarButtons(nsWindow);
            Data.ConfigEntry.PropertyChanged += ConfigEntry_OnPropertyChanged;
            Closed += (_, _) => Data.ConfigEntry.PropertyChanged -= ConfigEntry_OnPropertyChanged;
        }
    }

    private void ConfigEntry_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        
        if (e.PropertyName != nameof(Data.ConfigEntry.Theme) || _macOsWindowHandle == IntPtr.Zero)
            return;
        RefreshMacOsTitleBarButtons(_macOsWindowHandle);
    }

    private static void RefreshMacOsTitleBarButtons(IntPtr nsWindow)
    {
        try
        {
            
            
            TioUi.Common.Helpers.MacOsWindowHandler.RefreshTitleBarButtonPosition(nsWindow, x: 14, y: 2,
                spacing: 20);
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }
    }

    private void GoToStep(int step)
    {
        step = Math.Clamp(step, 0, STEP_COUNT - 1);
        Steps.SelectedIndex = step;
        BackButton.IsEnabled = step > 0;
        NextButton.IsVisible = step < STEP_COUNT - 1;
        FinishButton.IsVisible = step == STEP_COUNT - 1;
        StepTitle.Text = step switch
        {
            0 => "主题样式",
            1 => "Minecraft 文件夹",
            2 => "账户",
            3 => "Java",
            _ => string.Empty
        };
        UpdateStepDots(step);
    }

        private void UpdateStepDots(int step)
    {
        var dots = StepDots.Children;
        var count = dots.Count;
        for (var i = 0; i < count; i++)
            dots[i].Classes.Set("active", i == step);

        var fromWidth = new double[count];
        var fromOpacity = new double[count];
        var toWidth = new double[count];
        var toOpacity = new double[count];
        for (var i = 0; i < count; i++)
        {
            fromWidth[i] = double.IsNaN(dots[i].Width) ? 6 : dots[i].Width;
            fromOpacity[i] = dots[i].Opacity;
            toWidth[i] = i == step ? 18 : 6;
            toOpacity[i] = i == step ? 1 : 0.3;
        }

        var token = ++_dotsAnimationToken;
        if (!IsLoaded)
        {
            Apply(1);
            return;
        }

        TimeSpan? startTime = null;
        RequestAnimationFrame(Frame);
        return;

        void Frame(TimeSpan now)
        {
            if (token != _dotsAnimationToken) return;
            startTime ??= now;
            var progress = Math.Clamp((now - startTime.Value).TotalMilliseconds / 200.0, 0, 1);
            Apply(progress >= 1 ? 1 : DotsEasing.Ease(progress));
            if (progress < 1) RequestAnimationFrame(Frame);
        }

        void Apply(double eased)
        {
            for (var i = 0; i < count; i++)
            {
                dots[i].Width = fromWidth[i] + (toWidth[i] - fromWidth[i]) * eased;
                dots[i].Opacity = Math.Clamp(fromOpacity[i] + (toOpacity[i] - fromOpacity[i]) * eased, 0, 1);
            }
        }
    }

    private void Back_OnClick(object? sender, RoutedEventArgs e)
    {
        GoToStep(Steps.SelectedIndex - 1);
    }

    private void Next_OnClick(object? sender, RoutedEventArgs e)
    {
        GoToStep(Steps.SelectedIndex + 1);
    }

    private void Finish_OnClick(object? sender, RoutedEventArgs e)
    {
        FinishButton.IsEnabled = false;
        Completed?.Invoke();
    }

    private async void AddFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        var options = new OverlayDialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            VerticalOffset = 60,
            VerticalAnchor = VerticalPosition.Top
        };

        var result = await OverlayDialog
            .ShowCustomAsync<NewMinecraftFolder, NewMinecraftFolderViewModel, MinecraftFolderEntry>(
                new NewMinecraftFolderViewModel(Data.ConfigEntry.MinecraftFolders.Select(x
                    => x.FolderPath).ToList()), hostId: HostId, options: options);

        if (result == null) return;
        Data.ConfigEntry.MinecraftFolders.Add(result);
    }

    private async void AddAccount_OnClick(object? sender, RoutedEventArgs e)
    {
        var result = await AddAccount.Main(HostId, Data.ConfigEntry.AuthServers);
        if (result == null) return;
        foreach (var minecraftAccount in result.JavaAccounts)
        {
            Data.ConfigEntry.MinecraftAccounts.Add(minecraftAccount);
        }
        if (result.JavaAccounts.Count > 0)
            Data.ConfigEntry.UsingMinecraftMinecraftAccount = result.JavaAccounts[^1];
        if (result.BedrockAccount is { } bedrockAccount)
        {
            var existing = Data.ConfigEntry.BedrockAccounts.FirstOrDefault(item => item.Xuid == bedrockAccount.Xuid);
            if (existing != null) Data.ConfigEntry.BedrockAccounts.Remove(existing);
            Data.ConfigEntry.BedrockAccounts.Add(bedrockAccount);
            Data.ConfigEntry.UsingBedrockAccount = bedrockAccount;
        }
    }

    private async void ScanJava_OnClick(object? sender, RoutedEventArgs e)
    {
        SetJavaBusy(true);
        NotificationGateway.Notice(this, "正在扫描中", NotificationType.Information);
        ShowJavaStatus("正在扫描 Java，请稍候…");
        try
        {
            var result = await JavaRuntimeOperations.ScanAndAddAsync(Data.ConfigEntry.JavaRuntimes);
            Data.ConfigEntry.DefaultJavaRuntime ??= Data.ConfigEntry.JavaRuntimes.FirstOrDefault();
            ShowJavaStatus($"扫描完成：新增 {result.AddedCount} 个 Java，重复 {result.DuplicateCount} 个");
        }
        catch (Exception ex)
        {
            ShowJavaStatus($"Java 扫描失败：{ex.Message}");
        }
        finally
        {
            SetJavaBusy(false);
        }
    }

    private async void AddJava_OnClick(object? sender, RoutedEventArgs e)
    {
        SetJavaBusy(true);
        try
        {
            var result = await JavaRuntimeOperations.AddFromPickerAsync(this, Data.ConfigEntry.JavaRuntimes);
            if (result == null) return;

            if (!result.IsValid)
            {
                ShowJavaStatus("无法识别该 Java 可执行文件");
                return;
            }

            Data.ConfigEntry.DefaultJavaRuntime ??= result.JavaRuntime;
            ShowJavaStatus(result.IsDuplicate ? "该 Java 已在列表中" : $"已添加 {result.JavaRuntime!.DisplayName}");
        }
        catch (Exception ex)
        {
            ShowJavaStatus($"添加 Java 失败：{ex.Message}");
        }
        finally
        {
            SetJavaBusy(false);
        }
    }

    private void SetJavaBusy(bool isBusy)
    {
        ScanJavaButton.IsEnabled = !isBusy;
        AddJavaButton.IsEnabled = !isBusy;
    }

    private void ShowJavaStatus(string message)
    {
        JavaStatusText.Text = message;
        JavaStatusText.IsVisible = true;
    }
}
