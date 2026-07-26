using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Interactivity;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Operations.Account;
using Portal.Core.Operations.Java;
using Portal.Core.Operations.OpenFile;
using Portal.Module.Animations;
using Tio.Avalonia.Standard.Modules.DiskIO;
using TioUi.Common;
using TioUi.Controls;

namespace Portal.Views;

public partial class OobeWindow : TioWindow
{
    private const int STEP_COUNT = 4;

    private static readonly SoftBackEaseOut DotsEasing = new() { Amplitude = 0.6 };

    private IntPtr _macOsWindowHandle;
    private int _dotsAnimationToken;

    /// <summary>
    /// 用户在最后一步点击「进入 Portal」后触发，由 App 负责切换到主窗口
    /// </summary>
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
        // 切换主题会重建标题栏，需要重新调整红绿灯位置
        if (e.PropertyName != nameof(Data.ConfigEntry.Theme) || _macOsWindowHandle == IntPtr.Zero)
            return;
        RefreshMacOsTitleBarButtons(_macOsWindowHandle);
    }

    private static void RefreshMacOsTitleBarButtons(IntPtr nsWindow)
    {
        try
        {
            // 初始化窗口为固定大小，隐藏缩放（最大化）按钮，位置与崩溃窗口保持一致
            TioUi.Common.Helpers.MacOsWindowHandler.HideZoomButton(nsWindow);
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

    /// <summary>
    /// 用同一个时钟同时驱动所有小圆点的宽度与透明度。
    /// 若改用两个独立的样式 Transition（一个收一个放），二者的启动时机与逐帧取值无法严格互补，
    /// 动画期间总宽度会多出约 1px，把右侧的点顶偏，结束时再弹回来
    /// </summary>
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
        if (result == null || result.Length == 0) return;
        foreach (var minecraftAccount in result)
        {
            if (minecraftAccount is null) continue;
            Data.ConfigEntry.MinecraftAccounts.Add(minecraftAccount);
        }

        if (result.Length == 1 && result[0] == null) return;
        Data.ConfigEntry.UsingMinecraftMinecraftAccount = result.LastOrDefault();
    }

    private async void ScanJava_OnClick(object? sender, RoutedEventArgs e)
    {
        SetJavaBusy(true);
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
