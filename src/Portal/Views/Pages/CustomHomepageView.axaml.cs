using System.Net.Http;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Loader;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Portal.Core.Classes;
using Portal.Core.Const;
using Portal.Core.Module.Initialize;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Views.Pages;

public partial class CustomHomepageView : UserControl
{
    private static readonly HttpClient Client = CreateHomepageClient();
    private static CustomHomepageView? _current;
    private static event Action? RefreshRequested;
    private int _loadVersion;
    private CancellationTokenSource? _refreshCts;
    private FileSystemWatcher? _watcher;

    public static readonly string LocalHomepagePath = Path.Combine(ConfigPath.UserDataRootPath, "PCL", "Custom.xaml");

    public static IReadOnlyList<string> PresetNames =>
    [
        SettingsLanguageManager.Instance.applicationdebug_customHomepagePresetTrivia.CurrentValue(),
        SettingsLanguageManager.Instance.applicationdebug_customHomepagePresetEchoCave.CurrentValue(),
        SettingsLanguageManager.Instance.applicationdebug_customHomepagePresetMcNews.CurrentValue(),
        SettingsLanguageManager.Instance.applicationdebug_customHomepagePresetDailyModpack.CurrentValue(),
        SettingsLanguageManager.Instance.applicationdebug_customHomepagePresetMcSkin.CurrentValue(),
        SettingsLanguageManager.Instance.applicationdebug_customHomepagePresetOpenBmclapi.CurrentValue(),
        SettingsLanguageManager.Instance.applicationdebug_customHomepagePresetPclManual.CurrentValue(),
        SettingsLanguageManager.Instance.applicationdebug_customHomepagePresetMagazine.CurrentValue(),
        SettingsLanguageManager.Instance.applicationdebug_customHomepagePresetGithub.CurrentValue(),
        SettingsLanguageManager.Instance.applicationdebug_customHomepagePresetMcUpdate.CurrentValue(),
        SettingsLanguageManager.Instance.applicationdebug_customHomepagePresetNewsToday.CurrentValue(),
        SettingsLanguageManager.Instance.applicationdebug_customHomepagePresetKnowledge.CurrentValue(),
        SettingsLanguageManager.Instance.applicationdebug_customHomepagePresetModpack.CurrentValue(),
        SettingsLanguageManager.Instance.applicationdebug_customHomepagePresetBangumi.CurrentValue(),
        // SettingsLanguageManager.Instance.applicationdebug_customHomepagePresetAnnouncement.CurrentValue(),
        // SettingsLanguageManager.Instance.applicationdebug_customHomepagePresetOfficialFeed.CurrentValue()
    ];

    private static readonly string[] PresetUrls =
    [
        "", "", "https://news.bugjump.net", "https://pclsub.sodamc.com/", "https://forgepixel.com/pcl_sub_file",
        "https://pcl-bmcl.milu.ink/", "https://raw.gitcode.com/WForst-Breeze/WhatsNewPCL/raw/main/Custom.xaml",
        "https://pclhomeplazaoss.lingyunawa.top:26994/d/Homepages/Ext1nguisher/Custom.xaml",
        "https://ddf.pcl-community.org/Custom.xaml",
        "https://raw.gitcode.com/ENC_Euphony/PCL-AI-Summary-HomePage/raw/master/Custom.xaml",
        "https://pcl.wyc-w.top/index.xaml", "https://www.xxag.top/mkss",
        "https://qawsedrftgyhujiko.fun/pcl2/Custom.xaml", "https://bangumi.p.kaphia.qzz.io",
        "https://s3.pysio.online/pcl2-ce/apiv2/pages/announce.xaml", ""
    ];

    private static HttpClient CreateHomepageClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // news.bugjump.net and several PCL-CE presets gate their XAML response on a PCL UA.
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "PCL2/2.8 PCLCE/1 Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36 Edg/136.0.0.0");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/xaml, application/xml, text/plain, */*");
        return client;
    }

    public CustomHomepageView()
    {
        InitializeComponent();
        _current = this;
        AttachedToVisualTree += (_, _) =>
        {
            Data.ConfigEntry.PropertyChanged += ConfigEntryOnPropertyChanged;
            RefreshRequested += OnRefreshRequested;
            EnsureWatcher();
            _ = RefreshAsync();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            Data.ConfigEntry.PropertyChanged -= ConfigEntryOnPropertyChanged;
            RefreshRequested -= OnRefreshRequested;
            if (ReferenceEquals(_current, this)) _current = null;
        };
    }

    public static void RequestRefresh() => RefreshRequested?.Invoke();

    private void OnRefreshRequested() => _ = RefreshAsync(true);

    private void ConfigEntryOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Data.ConfigEntry.CustomHomepageType)
            or nameof(Data.ConfigEntry.CustomHomepagePreset)
            or nameof(Data.ConfigEntry.CustomHomepageUrl))
            Dispatcher.UIThread.Post(() => _ = RefreshAsync(true));
    }

    private async Task RefreshAsync(bool force = false)
    {
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();
        var cancellationToken = _refreshCts.Token;
        var version = ++_loadVersion;
        try
        {
            var config = Data.ConfigEntry;
            string content = config.CustomHomepageType switch
            {
                1 => await ReadLocalAsync(cancellationToken),
                2 => await LoadRemoteAsync(config.CustomHomepageUrl, cancellationToken),
                3 => await LoadPresetAsync(config.CustomHomepagePreset),
                _ => string.Empty
            };
            if (version != _loadVersion) return;
            ContentHost.Children.Clear();
            if (string.IsNullOrWhiteSpace(content)) return;
            ContentHost.Children.Add(ParseContent(content));
        }
        catch (Exception ex)
        {
            Logger.Warning($"[Homepage] Failed to load custom homepage: {ex.Message}");
            ContentHost.Children.Clear();
            ContentHost.Children.Add(new TextBlock
                { Text = ex.Message, Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap });
        }
    }

    private static async Task<string> ReadLocalAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(LocalHomepagePath)) return string.Empty;
        return await File.ReadAllTextAsync(LocalHomepagePath, cancellationToken);
    }

    private static async Task<string> LoadRemoteAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        var cache = Path.Combine(ConfigPath.CacheFolderPath, "CustomHomepage.xaml");
        Directory.CreateDirectory(ConfigPath.CacheFolderPath);
        try
        {
            var text = await Client.GetStringAsync(url, cancellationToken);
            await File.WriteAllTextAsync(cache, text);
            return text;
        }
        catch when (File.Exists(cache))
        {
            return await File.ReadAllTextAsync(cache);
        }
    }

    private static async Task<string> LoadPresetAsync(int index)
    {
        index = Math.Clamp(index, 0, PresetUrls.Length - 1);
        if (index == 0)
            return
                "<pcl:MyCard Title=\"Trivia\" xmlns:pcl=\"clr-namespace:Portal.Views.Pages;assembly=Portal\"><TextBlock Text=\"Minecraft\" Margin=\"20\" TextWrapping=\"Wrap\" /></pcl:MyCard>";
        if (index == 1) return string.Empty;
        if (index == 15)
            return
                "<pcl:MyCard Title=\"Minecraft Official Feed\" xmlns:pcl=\"clr-namespace:Portal.Views.Pages;assembly=Portal\"><TextBlock Text=\"Minecraft news feed\" Margin=\"20\" /></pcl:MyCard>";
        return await LoadRemoteAsync(PresetUrls[index]);
    }

    private static Control ParseContent(string content)
    {
        // PCL files commonly use the WPF `local:` prefix without declaring it in the fragment.
        // Normalize that prefix before parsing so malformed namespace declarations do not reject
        // otherwise valid homepage fragments.
        content = Regex.Replace(content, "\\blocal:", "pcl:", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, "<\\?xml[^>]*\\?>", string.Empty, RegexOptions.IgnoreCase);
        content = StripDoctypeDeclarations(content);
        if (Regex.IsMatch(content, @"<\s*(?:html|head|meta|!doctype\s+html)\b", RegexOptions.IgnoreCase))
            return CreateHtmlFallback(content);
        var xml = XDocument.Parse(
            $"<root xmlns:pcl=\"urn:portal-pcl\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:sys=\"clr-namespace:System;assembly=System.Runtime\" xmlns:d=\"http://schemas.microsoft.com/expression/blend/2008\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\">{content}</root>");
        var blocked = new[]
            { "ObjectDataProvider", "WebBrowser", "Frame", "MediaElement", "Window", "XamlReader", "XmlDataProvider" };
        if (xml.Descendants().Any(e => blocked.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Unsupported or unsafe homepage control.");
        var wpfDocumentTags = new[] { "FlowDocument", "FlowDocumentScrollViewer", "Paragraph", "ListItem" };
        if (xml.Descendants().Any(e => wpfDocumentTags.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase)) ||
            xml.Descendants().Any(e => e.Name.LocalName.Equals("Style", StringComparison.OrdinalIgnoreCase) &&
                                       e.Attribute("TargetType") is not null))
            return CreateXamlTextFallback(xml);
        content = Regex.Replace(content, "xmlns(:\\w+)?=\\\"[^\\\"]*\\\"", string.Empty, RegexOptions.IgnoreCase);
        content = Regex.Replace(content,
            "\\s+(x:Class|x:Name|Name|FactoryMethod|Code|StaticResource|DynamicResource)=\\\"[^\\\"]*\\\"",
            string.Empty, RegexOptions.IgnoreCase);
        content = Regex.Replace(content,
            "\\s+(Style|Template|FocusVisualStyle|SnapsToDevicePixels|UseLayoutRounding|RenderTransformOrigin|TextOptions\\.[A-Za-z]+|RenderOptions\\.[A-Za-z]+|ToolTipService\\.[A-Za-z]+)=\\\"[^\\\"]*\\\"",
            string.Empty, RegexOptions.IgnoreCase);
        content = Regex.Replace(content, "\\s+(?:local|pcl):CustomEventService\\.[A-Za-z]+\\s*=\\s*\\\"[^\\\"]*\\\"",
            string.Empty, RegexOptions.IgnoreCase);
        content = Regex.Replace(content, "<pcl:CustomEvent\\b[^>]*/>", string.Empty, RegexOptions.IgnoreCase);
        content = Regex.Replace(content, "<pcl:CustomEvent\\b[^>]*>.*?</pcl:CustomEvent>", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        content =
            $"<StackPanel xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:sys=\"clr-namespace:System;assembly=System.Runtime\" xmlns:d=\"http://schemas.microsoft.com/expression/blend/2008\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:pcl=\"clr-namespace:Portal.Views.Pages;assembly=Portal\">{content}</StackPanel>";
        return (Control)AvaloniaRuntimeXamlLoader.Load(content, typeof(CustomHomepageView).Assembly, null, null, true);
    }

    private static Control CreateHtmlFallback(string html)
    {
        html = Regex.Replace(html, @"<script\b[^>]*>.*?</script>", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        html = Regex.Replace(html, @"<style\b[^>]*>.*?</style>", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var text = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " "));
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length > 4000) text = text[..4000] + "…";
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(28, 128, 128, 128)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(18),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Web homepage", FontSize = 16, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray }
                }
            }
        };
    }

    private static Control CreateXamlTextFallback(XDocument document)
    {
        var text = Regex.Replace(document.Root?.Value ?? string.Empty, @"\s+", " ").Trim();
        if (text.Length > 12000) text = text[..12000] + "…";
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(28, 128, 128, 128)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(18),
            Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }
        };
    }

    private static string StripDoctypeDeclarations(string content)
    {
        while (true)
        {
            var start = content.IndexOf("<!DOCTYPE", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return content;

            var subsetStart = content.IndexOf('[', start);
            var end = subsetStart >= 0
                ? content.IndexOf("]>", subsetStart, StringComparison.Ordinal)
                : content.IndexOf('>', start);
            if (end < 0) return content[..start];
            end += subsetStart >= 0 ? 2 : 1;
            content = content.Remove(start, end - start);
        }
    }

    private void EnsureWatcher()
    {
        if (_watcher is not null) return;
        var directory = Path.GetDirectoryName(LocalHomepagePath)!;
        Directory.CreateDirectory(directory);
        _watcher = new FileSystemWatcher(directory, Path.GetFileName(LocalHomepagePath))
        {
            EnableRaisingEvents = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
        };
        void QueueRefresh() => Dispatcher.UIThread.Post(() => _ = RefreshAsync(true));
        _watcher.Changed += (_, _) => QueueRefresh();
        _watcher.Created += (_, _) => QueueRefresh();
        _watcher.Renamed += (_, _) => QueueRefresh();
    }
}

public class MyCard : ContentControl
{
    public static readonly AvaloniaProperty TitleProperty =
        AvaloniaProperty.Register<MyCard, string>(nameof(Title), string.Empty);

    public string Title
    {
        get => (string?)GetValue(TitleProperty) ?? string.Empty;
        set => SetValue(TitleProperty, value);
    }
    public bool CanSwap { get; set; }
    public bool IsSwapped { get; set; }
    public bool IsSwaped { get => IsSwapped; set => IsSwapped = value; }

    public MyCard()
    {
        Background = new SolidColorBrush(Color.FromArgb(28, 128, 128, 128));
        BorderBrush = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128));
        BorderThickness = new Avalonia.Thickness(1);
        CornerRadius = new Avalonia.CornerRadius(6);
        Padding = new Avalonia.Thickness(12);
        Margin = new Avalonia.Thickness(0, 0, 0, 12);
    }
}

public class MyButton : Button
{
    public string EventType { get; set; } = string.Empty;
    public string EventData { get; set; } = string.Empty;
    public string Logo { get; set; } = string.Empty;
    public double LogoScale { get; set; } = 1;
    public string ColorType { get; set; } = string.Empty;
    public string Text
    {
        get => Content?.ToString() ?? string.Empty;
        set => Content = value;
    }
}

public class MyTextButton : Button
{
    public string EventType { get; set; } = string.Empty;
    public string EventData { get; set; } = string.Empty;
    public string Text
    {
        get => Content?.ToString() ?? string.Empty;
        set => Content = value;
    }
}

public class MyIconButton : Button
{
    public string SvgIcon { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string EventData { get; set; } = string.Empty;
    public double LogoScale { get; set; } = 1;
}

public class MyIconTextButton : Button
{
    public string EventType { get; set; } = string.Empty;
    public string EventData { get; set; } = string.Empty;
    public string Logo { get; set; } = string.Empty;
    public double LogoScale { get; set; } = 1;
    public string Text
    {
        get => Content?.ToString() ?? string.Empty;
        set => Content = value;
    }

    public string SvgIcon { get; set; } = string.Empty;
}

public class MyExtraButton : Button
{
    public string EventType { get; set; } = string.Empty;
    public string EventData { get; set; } = string.Empty;
    public string Text
    {
        get => Content?.ToString() ?? string.Empty;
        set => Content = value;
    }
}

public class MyExtraTextButton : Button
{
    public string EventType { get; set; } = string.Empty;
    public string EventData { get; set; } = string.Empty;
    public string Text
    {
        get => Content?.ToString() ?? string.Empty;
        set => Content = value;
    }
}

public class MyListItem : Border
{
    public string Title { get; set; } = string.Empty;
    public string Info { get; set; } = string.Empty;
    public string Logo { get; set; } = string.Empty;
    public string SvgIcon { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string EventData { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public double LogoScale { get; set; } = 1;
}

public class MyImage : Image
{
    public string FallbackSource { get; set; } = string.Empty;
    public string LoadingSource { get; set; } = string.Empty;
    public bool EnableCache { get; set; } = true;
    public double LogoScale { get; set; } = 1;
    public new string Source
    {
        get => _source;
        set
        {
            _source = value ?? string.Empty;
            if (File.Exists(_source))
                base.Source = new Avalonia.Media.Imaging.Bitmap(_source);
        }
    }

    private string _source = string.Empty;

    public string ActualSource
    {
        get => _source;
        set => Source = value;
    }
}

public class MyHint : Border
{
    public string Text
    {
        get => (Child as TextBlock)?.Text ?? string.Empty;
        set
        {
            Child = new TextBlock
                { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(12) };
        }
    }
}

public class MyLoading : ProgressBar
{
    public MyLoading()
    {
        IsIndeterminate = true;
        Height = 4;
    }
}

public class MyTextBox : TextBox
{
    public string HintText
    {
        get => PlaceholderText ?? string.Empty;
        set => PlaceholderText = value;
    }
}

public class MyComboBox : ComboBox
{
}

public class MyComboBoxItem : ComboBoxItem
{
}

public class MyCheckBox : CheckBox
{
    public bool? Checked
    {
        get => IsChecked;
        set => IsChecked = value;
    }
}

public class MyRadioBox : RadioButton
{
    public string Text
    {
        get => Content?.ToString() ?? string.Empty;
        set => Content = value;
    }
}

public class MyRadioButton : RadioButton
{
}

public class MyScrollViewer : ScrollViewer
{
}

public class MySlider : Slider
{
}

public class MySearchBox : AutoCompleteBox
{
}

public class MyCollapseBar : Expander
{
}

public class MyMenuItem : MenuItem
{
}

public class MinecraftServer : Border
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}

public class MinecraftServerQuery : Border
{
    public string Address { get; set; } = string.Empty;
}

public class MyMsgBox : ContentControl
{
}

public class MyMsgTextBox : TextBox
{
}

public class MyMsgComboBox : ComboBox
{
}
