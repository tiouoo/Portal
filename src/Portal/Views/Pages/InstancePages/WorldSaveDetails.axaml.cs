using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Portal.Core.Module;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class WorldSaveDetails : UserControl
{
    public WorldSaveDetails()
    {
        InitializeComponent();
    }

    private void NavMenu_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if ((sender as NavMenu)?.SelectedItem is NavMenuItem { Tag: WorldSaveDetailsPage page })
            ((WorldSaveDetailsViewModel)DataContext).SelectedPage = page;
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e)
    {
        ((WorldSaveDetailsViewModel)DataContext).Close();
    }
}

public partial class WorldSaveOverview : UserControl
{
    public WorldSaveOverview()
    {
        InitializeComponent();
    }
}

public partial class WorldSaveGameRules : UserControl
{
    public WorldSaveGameRules()
    {
        InitializeComponent();
    }
}

public partial class WorldSaveWeather : UserControl
{
    public WorldSaveWeather()
    {
        InitializeComponent();
    }
}

public partial class WorldSaveClocks : UserControl
{
    public WorldSaveClocks()
    {
        InitializeComponent();
    }
}

public partial class WorldSaveScoreboard : UserControl
{
    public WorldSaveScoreboard()
    {
        InitializeComponent();
    }

    private void RemoveObjective_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is WorldScoreboardObjectiveSetting objective &&
            DataContext is WorldSaveDetailsViewModel viewModel)
            viewModel.ScoreboardObjectives.Remove(objective);
    }

    private void RemoveScore_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is WorldScoreboardScoreSetting score &&
            DataContext is WorldSaveDetailsViewModel viewModel)
            viewModel.ScoreboardScores.Remove(score);
    }
}

public partial class WorldSavePlayers : UserControl
{
    public WorldSavePlayers()
    {
        InitializeComponent();
    }
}

public enum WorldSaveDetailsPage
{
    Overview,
    GameRules,
    Weather,
    Clocks,
    Players,
    Scoreboard
}

public partial class WorldSaveDetailsViewModel : ObservableObject, IDialogContext
{
    private readonly WorldEnvironmentService _environmentService = new();
    private readonly WorldGameRuleService _gameRuleService = new();
    private readonly WorldSaveInfo _info;
    private readonly MinecraftInstance _instance;
    private readonly WorldLevelDataService _levelDataService = new();
    private readonly Dictionary<WorldSaveDetailsPage, UserControl> _pages;
    private readonly WorldPlayerDataService _playerDataService = new();
    private readonly WorldScoreboardService _scoreboardService = new();
    private readonly WorldSaveService _worldSaveService = new();
    [ObservableProperty] private bool _allowCheats;
    [ObservableProperty] private string _clearWeatherTime = "0";
    [ObservableProperty] private UserControl? _currentPage;
    [ObservableProperty] private bool _hasClocks;
    [ObservableProperty] private bool _hasGameRules;
    [ObservableProperty] private bool _hasLevelSettings;
    [ObservableProperty] private bool _hasPlayers;
    [ObservableProperty] private bool _hasScoreboard;
    [ObservableProperty] private bool _hasWeather;
    [ObservableProperty] private bool _isLoading = true;
    private WorldLevelData? _levelData;
    [ObservableProperty] private string _rainTime = "0";
    [ObservableProperty] private bool _raining;
    private WorldGameRules? _rules;
    [ObservableProperty] private int _selectedDifficulty = -1;
    [ObservableProperty] private int _selectedGameMode = -1;

    [ObservableProperty] private WorldSaveDetailsPage _selectedPage;
    [ObservableProperty] private string _thunderTime = "0";
    [ObservableProperty] private bool _thundering;
    [ObservableProperty] private string _worldSeed = string.Empty;

    public WorldSaveDetailsViewModel(WorldSaveInfo info, MinecraftInstance instance)
    {
        _info = info;
        _instance = instance;
        _pages = new Dictionary<WorldSaveDetailsPage, UserControl>
        {
            [WorldSaveDetailsPage.Overview] = new WorldSaveOverview(),
            [WorldSaveDetailsPage.GameRules] = new WorldSaveGameRules(),
            [WorldSaveDetailsPage.Weather] = new WorldSaveWeather(),
            [WorldSaveDetailsPage.Clocks] = new WorldSaveClocks(),
            [WorldSaveDetailsPage.Players] = new WorldSavePlayers(),
            [WorldSaveDetailsPage.Scoreboard] = new WorldSaveScoreboard()
        };
        CurrentPage = _pages[WorldSaveDetailsPage.Overview];
        _ = LoadAsync();
    }

    public ObservableCollection<WorldBooleanSetting> BooleanRules { get; } = [];
    public ObservableCollection<WorldNumberSetting> IntegerRules { get; } = [];
    public ObservableCollection<WorldNumberSetting> ClockSettings { get; } = [];
    public ObservableCollection<WorldScoreboardObjectiveSetting> ScoreboardObjectives { get; } = [];
    public ObservableCollection<WorldScoreboardScoreSetting> ScoreboardScores { get; } = [];
    public ObservableCollection<WorldPlayerDataSetting> Players { get; } = [];

    public IReadOnlyList<WorldGameModeOption> GameModeOptions { get; } =
        [new(0, "生存"), new(1, "创造"), new(2, "冒险"), new(3, "旁观")];

    public IReadOnlyList<WorldDifficultyOption> DifficultyOptions { get; } =
        [new(0, "和平"), new(1, "简单"), new(2, "普通"), new(3, "困难")];

    public string DisplayName => string.IsNullOrWhiteSpace(_info.LevelName) ? _info.FolderName : _info.LevelName;
    public string FolderName => _info.FolderName;
    public string CreationTime => _info.CreationTime.ToString("yyyy-MM-dd HH:mm");
    public string LastPlayedTime => _info.LastPlayedTime?.ToString("yyyy-MM-dd HH:mm") ?? "未知";
    public string Version => _info.Version ?? "未知";
    public string FileStatistics => $"{_info.PlayerDataCount} 个玩家数据，{_info.DataPackArchiveCount} 个数据包";
    public bool IsLocked => _info.IsLocked;
    public bool IsUnlocked => !IsLocked;
    public bool CanQuickEnter => _instance.MinecraftEntry is { } entry && entry.ReleaseTime > new DateTime(2023, 4, 4);

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;

    partial void OnSelectedPageChanged(WorldSaveDetailsPage value)
    {
        CurrentPage = _pages[value];
    }

    private async Task LoadAsync()
    {
        try
        {
            _rules = await _gameRuleService.LoadAsync(_info.FolderPath);
            if (_rules != null)
            {
                foreach (var (key, value) in _rules.BooleanRules.OrderBy(x => x.Key))
                    BooleanRules.Add(new WorldBooleanSetting(key, DisplayRuleName(key), value));
                foreach (var (key, value) in _rules.IntegerRules.OrderBy(x => x.Key))
                    IntegerRules.Add(new WorldNumberSetting(key, DisplayRuleName(key), value));
                HasGameRules = true;
            }

            var weather = await _environmentService.LoadWeatherAsync(_info.FolderPath);
            if (weather != null)
            {
                Raining = weather.Raining;
                Thundering = weather.Thundering;
                RainTime = weather.RainTime.ToString();
                ThunderTime = weather.ThunderTime.ToString();
                ClearWeatherTime = weather.ClearWeatherTime.ToString();
                HasWeather = true;
            }

            var clocks = await _environmentService.LoadClocksAsync(_info.FolderPath);
            if (clocks != null)
            {
                foreach (var (dimension, ticks) in clocks.TotalTicks.OrderBy(x => x.Key))
                    ClockSettings.Add(new WorldNumberSetting(dimension, dimension, ticks));
                HasClocks = true;
            }

            var scoreboard = await _scoreboardService.LoadAsync(_info.FolderPath);
            if (scoreboard != null)
            {
                foreach (var objective in scoreboard.Objectives)
                    ScoreboardObjectives.Add(new WorldScoreboardObjectiveSetting(objective.Name, objective.CriteriaName,
                        objective.DisplayName));
                foreach (var score in scoreboard.Scores)
                    ScoreboardScores.Add(new WorldScoreboardScoreSetting(score.Objective, score.Name, score.DisplayName,
                        score.Score, score.Locked));
                HasScoreboard = true;
            }

            foreach (var player in await _playerDataService.LoadAsync(_info.FolderPath))
                Players.Add(new WorldPlayerDataSetting(player));
            HasPlayers = Players.Count > 0;

            var levelData = await _levelDataService.LoadAsync(_info.FolderPath);
            if (levelData != null)
            {
                _levelData = levelData;
                SelectedGameMode = levelData.GameMode is >= 0 and <= 3 ? levelData.GameMode : -1;
                SelectedDifficulty = levelData.Difficulty is >= 0 and <= 3 ? levelData.Difficulty : -1;
                AllowCheats = levelData.AllowCommands;
                WorldSeed = levelData.Seed.ToString();
                HasLevelSettings = true;
            }
        }
        catch (Exception ex)
        {
            ShowNotice($"读取世界设置失败：{ex.Message}", NotificationType.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveGameRules()
    {
        if (_rules == null || !await CanSaveAsync()) return;
        if (!TryGetNumbers(IntegerRules, out var integers)) return;
        if (integers.Any(x => x.Value > int.MaxValue))
        {
            ShowNotice("游戏规则数值不能超过 2147483647", NotificationType.Warning);
            return;
        }

        _rules = new WorldGameRules(BooleanRules.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
            integers.ToDictionary(x => x.Key, x => (int)x.Value, StringComparer.Ordinal));
        await SaveAsync(() => _gameRuleService.SaveAsync(_info.FolderPath, _rules));
    }

    [RelayCommand]
    private async Task SaveWeather()
    {
        if (!HasWeather || !await CanSaveAsync()) return;
        if (!TryParseNonNegative(RainTime, out var rainTime) ||
            !TryParseNonNegative(ThunderTime, out var thunderTime) ||
            !TryParseNonNegative(ClearWeatherTime, out var clearWeatherTime))
        {
            ShowNotice("数值设置必须是非负整数", NotificationType.Warning);
            return;
        }

        if (rainTime > int.MaxValue || thunderTime > int.MaxValue || clearWeatherTime > int.MaxValue)
        {
            ShowNotice("天气时间不能超过 2147483647", NotificationType.Warning);
            return;
        }

        await SaveAsync(() => _environmentService.SaveWeatherAsync(_info.FolderPath,
            new WorldWeatherSettings(Raining, Thundering, (int)rainTime, (int)thunderTime, (int)clearWeatherTime)));
    }

    [RelayCommand]
    private async Task SaveClocks()
    {
        if (!HasClocks || !await CanSaveAsync()) return;
        if (!TryGetNumbers(ClockSettings, out var clocks)) return;
        await SaveAsync(() => _environmentService.SaveClocksAsync(_info.FolderPath,
            new WorldClockSettings(clocks.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal))));
    }

    [RelayCommand]
    private void AddObjective()
    {
        ScoreboardObjectives.Add(new WorldScoreboardObjectiveSetting("", "dummy", ""));
    }

    [RelayCommand]
    private void AddScore()
    {
        ScoreboardScores.Add(new WorldScoreboardScoreSetting("", "", "", 0, false));
    }

    [RelayCommand]
    private async Task SaveScoreboard()
    {
        if (!HasScoreboard || !await CanSaveAsync()) return;
        var objectives = ScoreboardObjectives.Select(x =>
            new WorldScoreboardObjective(x.Name.Trim(), x.CriteriaName.Trim(), x.DisplayName.Trim())).ToArray();
        if (objectives.Any(x => string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.CriteriaName)))
        {
            ShowNotice("积分榜目标名称和统计条件不能为空", NotificationType.Warning);
            return;
        }

        if (objectives.GroupBy(x => x.Name, StringComparer.Ordinal).Any(x => x.Count() > 1))
        {
            ShowNotice("积分榜目标名称不能重复", NotificationType.Warning);
            return;
        }

        var scores = new List<WorldScoreboardScore>();
        foreach (var setting in ScoreboardScores)
        {
            if (string.IsNullOrWhiteSpace(setting.Objective) || string.IsNullOrWhiteSpace(setting.Name) ||
                !int.TryParse(setting.Score, out var value))
            {
                ShowNotice("玩家分数需要目标、玩家名称和有效的 32 位整数分数", NotificationType.Warning);
                return;
            }

            if (!objectives.Any(x => x.Name == setting.Objective.Trim()))
            {
                ShowNotice("玩家分数引用了不存在的积分榜目标", NotificationType.Warning);
                return;
            }

            scores.Add(new WorldScoreboardScore(setting.Objective.Trim(), setting.Name.Trim(),
                setting.DisplayName.Trim(), value, setting.Locked));
        }

        if (scores.GroupBy(x => (x.Objective, x.Name)).Any(x => x.Count() > 1))
        {
            ShowNotice("同一积分榜目标中的玩家名称不能重复", NotificationType.Warning);
            return;
        }

        await SaveAsync(() => _scoreboardService.SaveAsync(_info.FolderPath, new WorldScoreboard(objectives, scores)));
    }

    [RelayCommand]
    private async Task SavePlayer(WorldPlayerDataSetting? player)
    {
        if (player == null || !await CanSaveAsync()) return;
        if (!player.TryCreate(out var data))
        {
            ShowNotice("玩家数据包含无效数值：模式为 0-3，饥饿和饱和度为 0-20，经验进度为 0-1。", NotificationType.Warning);
            return;
        }

        await SaveAsync(() => _playerDataService.SaveAsync(data));
    }

    [RelayCommand]
    private async Task SaveLevelSettings()
    {
        if (!HasLevelSettings || _levelData == null || !await CanSaveAsync()) return;
        if (SelectedGameMode is < 0 or > 3)
        {
            ShowNotice("请选择有效的游戏模式", NotificationType.Warning);
            return;
        }

        if (SelectedDifficulty is < 0 or > 3)
        {
            ShowNotice("请选择有效的难度", NotificationType.Warning);
            return;
        }

        if (!long.TryParse(WorldSeed, out var seed))
        {
            ShowNotice("世界种子必须是有效的 64 位整数", NotificationType.Warning);
            return;
        }

        await SaveAsync(() => _levelDataService.SaveAsync(_info.FolderPath,
            new WorldLevelData(SelectedGameMode, SelectedDifficulty, AllowCheats, seed)));
    }

    private async Task<bool> CanSaveAsync()
    {
        if (await _worldSaveService.IsWorldLockedAsync(_info.FolderPath))
        {
            ShowNotice("世界正在被 Minecraft 使用，不能保存更改", NotificationType.Warning);
            return false;
        }

        return true;
    }

    private bool TryGetNumbers(IEnumerable<WorldNumberSetting> settings,
        out IReadOnlyList<(string Key, long Value)> values)
    {
        var result = new List<(string Key, long Value)>();
        foreach (var setting in settings)
        {
            if (!TryParseNonNegative(setting.Value, out var value))
            {
                ShowNotice("数值设置必须是非负整数", NotificationType.Warning);
                values = [];
                return false;
            }

            result.Add((setting.Key, value));
        }

        values = result;
        return true;
    }

    private static bool TryParseNonNegative(string? text, out long value)
    {
        return long.TryParse(text, out value) && value >= 0;
    }

    private static string DisplayRuleName(string key)
    {
        return key.StartsWith("minecraft:", StringComparison.Ordinal) ? key["minecraft:".Length..] : key;
    }

    private async Task SaveAsync(Func<Task> save)
    {
        try
        {
            await save();
            ShowNotice("设置已保存", NotificationType.Success);
        }
        catch (IOException ex) when (IsFileLocked(ex))
        {
            ShowNotice("世界被 Minecraft 实例锁定，不能保存更改", NotificationType.Warning);
        }
        catch (IOException ex)
        {
            ShowNotice($"保存失败：{ex.Message}", NotificationType.Error);
        }
        catch (UnauthorizedAccessException)
        {
            ShowNotice("没有修改此世界设置的权限", NotificationType.Error);
        }
    }

    private static bool IsFileLocked(IOException exception)
    {
        return (exception.HResult & 0xffff) is 32 or 33;
    }

    [RelayCommand]
    private void QuickEnter()
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
            return;

        _ = MinecraftLaunchService.LaunchAsync(_instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(_instance, logSession => MinecraftLogPage.Open(logSession, topLevel)),
            BuildTarget());
    }

    [RelayCommand]
    private async Task CreateShortcut()
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
            return;

        await DesktopShortcutUi.CreateAsync(topLevel,
            () => DesktopShortcutService.CreateAsync(_instance, BuildTarget()));
    }

    private RecentPlayTarget BuildTarget()
    {
        return new RecentPlayTarget(
            _instance,
            RecentPlayTargetType.World,
            _info.FolderName,
            DisplayName,
            $"存档·{_info.Version ?? "未知版本"}·{GetGameModeText(_info.GameMode)}",
            _info.LastPlayedTime ?? DateTime.MinValue,
            _info.IconPath);
    }

    private static TopLevel? GetTopLevel()
    {
        return Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime { MainWindow: { } window }
            ? window
            : null;
    }

    private static string GetGameModeText(int? gameMode)
    {
        return gameMode switch
        {
            0 => "生存", 1 => "创造", 2 => "冒险", 3 => "旁观", _ => "未知模式"
        };
    }

    private void ShowNotice(string message, NotificationType type)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } window
            })
            window.Notice(message, type);
    }
}

public partial class WorldBooleanSetting(string key, string label, bool value) : ObservableObject
{
    [ObservableProperty] private bool _value = value;
    public string Key { get; } = key;
    public string Label { get; } = label;
}

public record WorldGameModeOption(int Value, string Display);

public record WorldDifficultyOption(int Value, string Display);

public partial class WorldNumberSetting(string key, string label, long value) : ObservableObject
{
    [ObservableProperty] private string _value = value.ToString();
    public string Key { get; } = key;
    public string Label { get; } = label;
}

public partial class WorldScoreboardObjectiveSetting(string name, string criteriaName, string displayName)
    : ObservableObject
{
    [ObservableProperty] private string _criteriaName = criteriaName;
    [ObservableProperty] private string _displayName = displayName;
    [ObservableProperty] private string _name = name;
}

public partial class WorldScoreboardScoreSetting(
    string objective,
    string name,
    string displayName,
    int score,
    bool locked) : ObservableObject
{
    [ObservableProperty] private string _displayName = displayName;
    [ObservableProperty] private bool _locked = locked;
    [ObservableProperty] private string _name = name;
    [ObservableProperty] private string _objective = objective;
    [ObservableProperty] private string _score = score.ToString();
}

public partial class WorldPlayerDataSetting : ObservableObject
{
    private readonly WorldPlayerData _source;
    [ObservableProperty] private string _dimension;
    [ObservableProperty] private string _experienceLevel;
    [ObservableProperty] private string _experienceProgress;
    [ObservableProperty] private string _experienceTotal;
    [ObservableProperty] private bool _flying;
    [ObservableProperty] private string _foodLevel;
    [ObservableProperty] private string _gameMode;
    [ObservableProperty] private string _health;
    [ObservableProperty] private bool _instabuild;
    [ObservableProperty] private bool _invulnerable;
    [ObservableProperty] private bool _mayFly;
    [ObservableProperty] private string _positionX;
    [ObservableProperty] private string _positionY;
    [ObservableProperty] private string _positionZ;
    [ObservableProperty] private string _saturation;

    public WorldPlayerDataSetting(WorldPlayerData source)
    {
        _source = source;
        _gameMode = source.GameMode.ToString();
        _health = source.Health.ToString();
        _foodLevel = source.FoodLevel.ToString();
        _saturation = source.Saturation.ToString();
        _experienceLevel = source.ExperienceLevel.ToString();
        _experienceTotal = source.ExperienceTotal.ToString();
        _experienceProgress = source.ExperienceProgress.ToString();
        _dimension = source.Dimension;
        _positionX = source.PositionX.ToString();
        _positionY = source.PositionY.ToString();
        _positionZ = source.PositionZ.ToString();
        _invulnerable = source.Invulnerable;
        _mayFly = source.MayFly;
        _flying = source.Flying;
        _instabuild = source.Instabuild;
    }

    public string PlayerId => _source.PlayerId;
    public string DataVersionDisplay => $"数据版本 {_source.DataVersion}";

    public bool TryCreate(out WorldPlayerData player)
    {
        if (!int.TryParse(GameMode, out var gameMode) || gameMode is < 0 or > 3 ||
            !float.TryParse(Health, out var health) || health < 0 ||
            !int.TryParse(FoodLevel, out var foodLevel) || foodLevel is < 0 or > 20 ||
            !float.TryParse(Saturation, out var saturation) || saturation is < 0 or > 20 ||
            !int.TryParse(ExperienceLevel, out var level) || level < 0 ||
            !int.TryParse(ExperienceTotal, out var total) || total < 0 ||
            !float.TryParse(ExperienceProgress, out var progress) || progress is < 0 or > 1 ||
            !double.TryParse(PositionX, out var x) || !double.TryParse(PositionY, out var y) ||
            !double.TryParse(PositionZ, out var z) || string.IsNullOrWhiteSpace(Dimension))
        {
            player = _source;
            return false;
        }

        player = _source with
        {
            GameMode = gameMode, Health = health, FoodLevel = foodLevel, Saturation = saturation,
            ExperienceLevel = level, ExperienceTotal = total, ExperienceProgress = progress,
            Dimension = Dimension.Trim(), PositionX = x, PositionY = y, PositionZ = z, Invulnerable = Invulnerable,
            MayFly = MayFly, Flying = Flying, Instabuild = Instabuild
        };
        return true;
    }
}