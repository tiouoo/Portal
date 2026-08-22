using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.LitematicaViewer.Enums;
using Portal.LitematicaViewer.Helpers;
using Portal.LitematicaViewer.Parsers;
using Portal.LitematicaViewer.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Tab.Interface;

using Portal.Module;
namespace Portal.Views.Pages;

public partial class LitematicaPage : UserControl, ITioTabPage
{
    private readonly LitematicaPageViewModel _vm;

    public LitematicaPage()
    {
        InitializeComponent();
        _vm = new LitematicaPageViewModel();
        DataContext = _vm;
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = CommonLanguageManager.Instance.litematica_pageTitle.CurrentValue(),
        IconGlyph = IconResources.GetGlyph("tag"), IconFont = IconResources.FontFamilyName
    };

    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        _vm.Release();
        DataContext = null;
    }

    private void ExportTxt_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
            _ = _vm.ExportTxtAsync(control);
    }

    private void ExportCsv_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
            _ = _vm.ExportCsvAsync(control);
    }
}

public partial class LitematicaPageViewModel : ObservableObject
{
    private AnalysisResult? _analysisResult;

    [ObservableProperty] private int _blockTypes;

    [ObservableProperty] private ObservableCollection<BlockEntry> _blocks = [];

    [ObservableProperty] private ObservableCollection<BlockCategoryFilter> _categories = [];
    [ObservableProperty] private string? _filePath;

    [ObservableProperty] private ObservableCollection<BlockEntry> _filteredBlocks = [];

    [ObservableProperty] private bool _hasData;

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private double _progress;
    private string? _projectName;

    [ObservableProperty] private BlockCategoryFilter? _selectedCategory;

    [ObservableProperty] private long _totalBlocks;

    public void Release()
    {
        _analysisResult = null;
        _projectName = null;
        Blocks = [];
        FilteredBlocks = [];
        Categories = [];
        SelectedCategory = null;
        HasData = false;
    }

    [RelayCommand]
    private async Task LoadAndAnalyze()
    {
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath)) return;

        IsLoading = true;
        Progress = 0;
        HasData = false;

        await Task.Run(() =>
        {
            var parser = new LitematicParser();
            var file = parser.Load(FilePath);
            _projectName = file.Name;

            var progress = new Progress<AnalysisProgress>(p => { Progress = p.Percent / 100.0; });

            var analysis = new AnalysisService();
            _analysisResult = analysis.Analyze(file, progress);

            TotalBlocks = _analysisResult.TotalBlocks;

            var list = _analysisResult.BlockCounts
                .OrderByDescending(kv => kv.Value)
                .Select(kv =>
                {
                    var nameCn = CnTranslateHelper.ToChinese(kv.Key);
                    var category = BlockCategoryHelper.Classify(kv.Key);
                    var percent = TotalBlocks > 0 ? (double)kv.Value / TotalBlocks : 0;
                    var units = UnitConverter.Convert(kv.Value);
                    return new BlockEntry(kv.Key, nameCn, kv.Value, category, percent, units);
                })
                .ToList();

            Blocks = new ObservableCollection<BlockEntry>(list);
            BlockTypes = list.Count;

            Categories = new ObservableCollection<BlockCategoryFilter>(
                new[] { new BlockCategoryFilter(null, CommonLanguageManager.Instance.litematica_all.CurrentValue()) }
                    .Concat(list.Select(b => b.Category).Distinct().OrderBy(c => c)
                        .Select(c => new BlockCategoryFilter(c, GetCategoryDisplayName(c))))
            );
            SelectedCategory = Categories[0];
        });

        HasData = true;
        IsLoading = false;
        ApplyFilter();
    }

    partial void OnSelectedCategoryChanged(BlockCategoryFilter? value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (SelectedCategory?.Category == null)
            FilteredBlocks = Blocks;
        else
            FilteredBlocks = new ObservableCollection<BlockEntry>(
                Blocks.Where(b => b.Category == SelectedCategory.Category));
    }

    public static string GetCategoryDisplayName(BlockCategory category)
    {
        return category switch
        {
            BlockCategory.Wool => CommonLanguageManager.Instance.litematica_categoryWool.CurrentValue(),
            BlockCategory.Wood => CommonLanguageManager.Instance.litematica_categoryWood.CurrentValue(),
            BlockCategory.Stone => CommonLanguageManager.Instance.litematica_categoryStone.CurrentValue(),
            BlockCategory.Concrete => CommonLanguageManager.Instance.litematica_categoryConcrete.CurrentValue(),
            BlockCategory.Glass => CommonLanguageManager.Instance.litematica_categoryGlass.CurrentValue(),
            BlockCategory.Terracotta => CommonLanguageManager.Instance.litematica_categoryTerracotta.CurrentValue(),
            BlockCategory.Redstone => CommonLanguageManager.Instance.litematica_categoryRedstone.CurrentValue(),
            BlockCategory.Container => CommonLanguageManager.Instance.litematica_categoryContainer.CurrentValue(),
            BlockCategory.Ore => CommonLanguageManager.Instance.litematica_categoryOre.CurrentValue(),
            BlockCategory.Iron => CommonLanguageManager.Instance.litematica_categoryIron.CurrentValue(),
            BlockCategory.Quartz => CommonLanguageManager.Instance.litematica_categoryQuartz.CurrentValue(),
            BlockCategory.Clay => CommonLanguageManager.Instance.litematica_categoryClay.CurrentValue(),
            BlockCategory.Prismarine => CommonLanguageManager.Instance.litematica_categoryPrismarine.CurrentValue(),
            BlockCategory.End => CommonLanguageManager.Instance.litematica_categoryEnd.CurrentValue(),
            BlockCategory.Nether => CommonLanguageManager.Instance.litematica_categoryNether.CurrentValue(),
            BlockCategory.Liquid => CommonLanguageManager.Instance.litematica_categoryLiquid.CurrentValue(),
            BlockCategory.Entity => CommonLanguageManager.Instance.litematica_categoryEntity.CurrentValue(),
            BlockCategory.Natural => CommonLanguageManager.Instance.litematica_categoryNatural.CurrentValue(),
            BlockCategory.OtherRock => CommonLanguageManager.Instance.litematica_categoryOtherRock.CurrentValue(),
            _ => category.ToString()
        };
    }

    public async Task ExportTxtAsync(Control sender)
    {
        if (_analysisResult == null) return;
        var path = await PickSavePath(sender, "txt", CommonLanguageManager.Instance.litematica_textFile.CurrentValue(),
            _projectName);
        if (path == null) return;
        new ExportService().Export(_analysisResult, path, ExportFormat.Txt);
        sender.AsTopLevel().Notice(CommonLanguageManager.Instance.litematica_exportedTxt.CurrentValue(),
            NotificationType.Success);
    }

    public async Task ExportCsvAsync(Control sender)
    {
        if (_analysisResult == null) return;
        var path = await PickSavePath(sender, "csv", CommonLanguageManager.Instance.litematica_csvFile.CurrentValue(),
            _projectName);
        if (path == null) return;
        new ExportService().Export(_analysisResult, path, ExportFormat.Csv);
        sender.AsTopLevel().Notice(CommonLanguageManager.Instance.litematica_exportedCsv.CurrentValue(),
            NotificationType.Success);
    }

    private static async Task<string?> PickSavePath(Control sender, string ext, string display,
        string? suggestedFileName)
    {
        var storage = TopLevel.GetTopLevel(sender)?.StorageProvider;
        if (storage == null) return null;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = string.Format(CommonLanguageManager.Instance.litematica_exportTitle.CurrentValue(), display),
            DefaultExtension = ext,
            SuggestedFileName = suggestedFileName ?? "export",
            FileTypeChoices = [new FilePickerFileType(display) { Patterns = [$"*.{ext}"] }]
        });
        return file?.TryGetLocalPath();
    }
}

public record BlockEntry(
    string BlockId,
    string NameCn,
    long Count,
    BlockCategory Category,
    double Percent,
    string Units)
{
    public string CategoryDisplay => LitematicaPageViewModel.GetCategoryDisplayName(Category);
}

public record BlockCategoryFilter(BlockCategory? Category, string DisplayText)
{
    public override string ToString()
    {
        return DisplayText;
    }
}