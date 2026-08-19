namespace Portal.Views.Pages.DownloadPages;

public partial class JavaResourceSearchView : ResourceSearchPageBase
{
    protected JavaResourceSearchView(JavaResourceSearchViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    /// <summary>Exposed for the XAML runtime loader; not intended for direct use.</summary>
    public JavaResourceSearchView() : this(new ModpackSearchPageViewModel())
    {
    }

    public JavaResourceSearchViewModel ViewModel { get; }
}
