using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LitematicaViewer.Core.Enums;
using LitematicaViewer.Core.Helpers;
using LitematicaViewer.Core.Parsers;
using LitematicaViewer.Core.Services;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Tab.Interface;

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
        Title = "Litematica 分析",
        Icon = StreamGeometry.Parse(
            "F1 M640,640z M0,0z M96.5,160L96.5,309.5C96.5,326.5,103.2,342.8,115.2,354.8L307.2,546.8C332.2,571.8,372.7,571.8,397.7,546.8L547.2,397.3C572.2,372.3,572.2,331.8,547.2,306.8L355.2,114.8C343.2,102.7,327,96,310,96L160.5,96C125.2,96,96.5,124.7,96.5,160z M208.5,176C226.2,176 240.5,190.3 240.5,208 240.5,225.7 226.2,240 208.5,240 190.8,240 176.5,225.7 176.5,208 176.5,190.3 190.8,176 208.5,176z")
    };

    public TabEntry HostTab { get; set; }

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
    [ObservableProperty] private string? _filePath;

    [ObservableProperty] private bool _hasData;

    [ObservableProperty] private long _totalBlocks;

    [ObservableProperty] private int _blockTypes;

    [ObservableProperty] private ObservableCollection<BlockEntry> _blocks = [];

    [ObservableProperty] private ObservableCollection<BlockEntry> _filteredBlocks = [];

    [ObservableProperty] private ObservableCollection<BlockCategoryFilter> _categories = [];

    [ObservableProperty] private BlockCategoryFilter? _selectedCategory;

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private double _progress;

    private AnalysisResult? _analysisResult;
    private string? _projectName;

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

            var progress = new Progress<AnalysisProgress>(p =>
            {
                Progress = p.Percent / 100.0;
            });

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
                    var units = UnitConverter.Convert((long)kv.Value);
                    return new BlockEntry(kv.Key, nameCn, kv.Value, category, percent, units);
                })
                .ToList();

            Blocks = new ObservableCollection<BlockEntry>(list);
            BlockTypes = list.Count;

            Categories = new ObservableCollection<BlockCategoryFilter>(
                new[] { new BlockCategoryFilter(null, "全部") }
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

    public static string GetCategoryDisplayName(BlockCategory category) => category switch
    {
        BlockCategory.Wool => "羊毛",
        BlockCategory.Wood => "木材",
        BlockCategory.Stone => "石料",
        BlockCategory.Concrete => "混凝土",
        BlockCategory.Glass => "玻璃",
        BlockCategory.Terracotta => "陶瓦",
        BlockCategory.Redstone => "红石",
        BlockCategory.Container => "容器",
        BlockCategory.Ore => "矿石",
        BlockCategory.Iron => "铁制品",
        BlockCategory.Quartz => "石英",
        BlockCategory.Clay => "黏土",
        BlockCategory.Prismarine => "海晶石",
        BlockCategory.End => "末地",
        BlockCategory.Nether => "下界",
        BlockCategory.Liquid => "液体",
        BlockCategory.Entity => "实体",
        BlockCategory.Natural => "自然",
        BlockCategory.OtherRock => "其他石料",
        _ => category.ToString()
    };

    public async Task ExportTxtAsync(Control sender)
    {
        if (_analysisResult == null) return;
        var path = await PickSavePath(sender, "txt", "文本文件", _projectName);
        if (path == null) return;
        new ExportService().Export(_analysisResult, path, ExportFormat.Txt);
        sender.AsTopLevel().Notice("已导出 TXT 文件", NotificationType.Success);
    }

    public async Task ExportCsvAsync(Control sender)
    {
        if (_analysisResult == null) return;
        var path = await PickSavePath(sender, "csv", "CSV 文件", _projectName);
        if (path == null) return;
        new ExportService().Export(_analysisResult, path, ExportFormat.Csv);
        sender.AsTopLevel().Notice("已导出 CSV 文件", NotificationType.Success);
    }

    private static async Task<string?> PickSavePath(Control sender, string ext, string display, string? suggestedFileName)
    {
        var storage = TopLevel.GetTopLevel(sender)?.StorageProvider;
        if (storage == null) return null;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"导出 {display}",
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
    public override string ToString() => DisplayText;
}
