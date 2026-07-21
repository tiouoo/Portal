using Avalonia.Controls;
using Avalonia.Input;

namespace Portal.Views.Pages.DownloadPages;

public partial class BedrockResourceSearchPage : UserControl
{
    protected BedrockResourceSearchPage(JavaResourceDefinition definition)
    {
        InitializeComponent(); DataContext = new BedrockResourceSearchViewModel(definition);
        Loaded += async (_, _) => await ((BedrockResourceSearchViewModel)DataContext).InitializeAsync();
    }

    public BedrockResourceSearchPage() : this(BedrockResourceDefinitions.BehaviorPack) { }
    private void SearchBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not BedrockResourceSearchViewModel viewModel) return;
        viewModel.SearchCommand.Execute(null); e.Handled = true;
    }
    private void Result_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed || (sender as Control)?.DataContext is not JavaResourceSearchResultItem item || TopLevel.GetTopLevel(this) is not { } topLevel) return;
        BedrockResourceDetailsPage.Open(topLevel, item.Target, item.Name); e.Handled = true;
    }
}

public sealed class BedrockBehaviorPackSearchPage() : BedrockResourceSearchPage(BedrockResourceDefinitions.BehaviorPack);
public sealed class BedrockResourcePackSearchPage() : BedrockResourceSearchPage(BedrockResourceDefinitions.ResourcePack);
public sealed class BedrockWorldSearchPage() : BedrockResourceSearchPage(BedrockResourceDefinitions.World);
public sealed class BedrockWorldTemplateSearchPage() : BedrockResourceSearchPage(BedrockResourceDefinitions.WorldTemplate);
