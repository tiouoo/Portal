using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Portal.Core.Const;
using Portal.Core.Module.AggregatedSearch;
using Portal.Core.Module.Ipc;
using Portal.Localization;
using Portal.Module.DefaultPage;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("pages_otherSettings", "pages_otherSettingsPath", "ApplicationDebug")]
public partial class ApplicationDebug : Dsc
{
    public ApplicationDebug()
    {
        InitializeComponent();
        DataContext = this;
    }

    public static IReadOnlyList<DefaultPageRegistry.DefaultPageEntry> DefaultPages => DefaultPageRegistry.Pages;

    public DefaultPageRegistry.DefaultPageEntry? SelectedDefaultPage
    {
        get => DefaultPages.FirstOrDefault(page => page.PageType.AssemblyQualifiedName == Data.ConfigEntry.DefaultPage);
        set
        {
            if (value != null)
                Data.ConfigEntry.DefaultPage = value.PageType.AssemblyQualifiedName!;
        }
    }

    public bool CanRegisterProtocol => ProtocolRegistration.IsSupported;
    public string RegisterProtocolButtonText => OperatingSystem.IsWindows()
        ? CommonLanguageManager.Instance.appDebug_writeRegistry.CurrentValue()
        : CommonLanguageManager.Instance.appDebug_registerProtocol.CurrentValue();
    public Logger.LogLevel[] LogLevels { get; } = Enum.GetValues<Logger.LogLevel>();

    private async void RegisterProtocol_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        try
        {
            await ProtocolRegistration.RegisterAsync();
            topLevel?.Notice(CommonLanguageManager.Instance.appDebug_registerSuccess.CurrentValue(),
                NotificationType.Success);
        }
        catch (OperationCanceledException)
        {
            topLevel?.Notice(CommonLanguageManager.Instance.appDebug_registerCancelled.CurrentValue(),
                NotificationType.Warning);
        }
        catch (Exception exception)
        {
            topLevel?.Notice(string.Format(
                CommonLanguageManager.Instance.appDebug_registerFailed.CurrentValue(), exception.Message),
                NotificationType.Error);
        }
    }
}