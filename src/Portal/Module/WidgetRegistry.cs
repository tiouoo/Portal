using Portal.Core.Module.Widgets;
using Portal.Localization;
using Portal.Views.Widgets;

namespace Portal.Module;

public sealed class WidgetDefinition
{
    private readonly List<(WidgetCellSize Size, Func<IWidgetContent> Factory)> _pages = [];
    public WidgetKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public WidgetCellSize DefaultSize { get; init; } = new(1, 1);
    public WidgetCategory Category { get; init; } = WidgetCategory.Utility;

    public IReadOnlyList<WidgetCellSize> SupportedSizes => _pages.Select(p => p.Size).ToList();

    public string SizeText => string.Join(" / ", _pages.Select(p => p.Size.ToString()));

    public WidgetDefinition AddPage(WidgetCellSize size, Func<IWidgetContent> factory)
    {
        _pages.Add((size, factory));
        return this;
    }

    public WidgetDefinition AddPage<TPage>(WidgetCellSize size) where TPage : IWidgetContent, new()
    {
        _pages.Add((size, () => new TPage()));
        return this;
    }

    public IWidgetContent Create(WidgetCellSize size)
    {
        var factory = _pages.FirstOrDefault(p => p.Size == size).Factory ?? _pages[0].Factory;
        var content = factory();
        content.Kind = Kind;
        return content;
    }

    public WidgetCellSize NearestSize(double width, double height)
    {
        var best = _pages[0].Size;
        var bestDistance = double.MaxValue;
        foreach (var (size, _) in _pages)
        {
            var dims = WidgetGeometry.GetSize(size);
            var distance = Math.Sqrt(Math.Pow(dims.Width - width, 2) + Math.Pow(dims.Height - height, 2));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = size;
            }
        }

        return best;
    }
}

public static class WidgetRegistry
{
    private static readonly Dictionary<WidgetKind, WidgetDefinition> _definitions = [];
    private static bool _initialized;

    public static IEnumerable<WidgetDefinition> Definitions
    {
        get
        {
            EnsureInitialized();
            return _definitions.Values;
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
            return;
        _initialized = true;
        RegisterBuiltins();
    }

    public static void Register(WidgetDefinition definition)
    {
        if (_definitions.ContainsKey(definition.Kind))
            throw new InvalidOperationException(string.Format(
                CommonLanguageManager.Instance.widgets_alreadyRegistered.CurrentValue(), definition.Kind));
        _definitions[definition.Kind] = definition;
    }

    public static WidgetDefinition? Get(WidgetKind kind)
    {
        EnsureInitialized();
        return _definitions.GetValueOrDefault(kind);
    }

    public static IWidgetContent Create(WidgetKind kind, WidgetCellSize size)
    {
        var definition = Get(kind) ?? throw new InvalidOperationException(string.Format(
            CommonLanguageManager.Instance.widgets_notRegistered.CurrentValue(), kind));
        return definition.Create(size);
    }

    private static void RegisterBuiltins()
    {
        Register(new WidgetDefinition
            {
                Kind = WidgetKind.Clock,
                Name = CommonLanguageManager.Instance.widgets_clock.CurrentValue(),
                Description = CommonLanguageManager.Instance.widgets_clockDescription.CurrentValue(),
                Category = WidgetCategory.Utility,
                DefaultSize = new WidgetCellSize(1, 1)
            }
            .AddPage<Clock1x1>(new WidgetCellSize(1, 1))
            .AddPage<Clock2x1>(new WidgetCellSize(2, 1)));


        var imageDef = new WidgetDefinition
        {
            Kind = WidgetKind.Image,
            Name = CommonLanguageManager.Instance.widgets_image.CurrentValue(),
            Description = CommonLanguageManager.Instance.widgets_imageDescription.CurrentValue(),
            Category = WidgetCategory.Utility,
            DefaultSize = new WidgetCellSize(2, 2)
        };
        for (var cols = 1; cols <= 16; cols++)
        for (var rows = 1; rows <= 16; rows++)
        {
            var size = new WidgetCellSize(cols, rows);
            imageDef.AddPage(size, () => new ImageViewWidget(size));
        }

        Register(imageDef);

        var searchDef = new WidgetDefinition
        {
            Kind = WidgetKind.Search,
            Name = CommonLanguageManager.Instance.widgets_search.CurrentValue(),
            Description = CommonLanguageManager.Instance.widgets_searchDescription.CurrentValue(),
            Category = WidgetCategory.Utility,
            DefaultSize = new WidgetCellSize(1, 4)
        };
        for (var column = 2; column <= 16; column++)
        {
            var size = new WidgetCellSize(column, 1);
            searchDef.AddPage(size, () => new SearchWidget(size));
        }

        Register(searchDef);

        Register(new WidgetDefinition
            {
                Kind = WidgetKind.Instance,
                Name = CommonLanguageManager.Instance.widgets_instance.CurrentValue(),
                Description = CommonLanguageManager.Instance.widgets_instanceDescription.CurrentValue(),
                Category = WidgetCategory.Game,
                DefaultSize = new WidgetCellSize(2, 1)
            }
            .AddPage<InstanceWidget1x1>(new WidgetCellSize(1, 1))
            .AddPage<InstanceWidget2x1>(new WidgetCellSize(2, 1)));

        Register(new WidgetDefinition
            {
                Kind = WidgetKind.QuickWorld,
                Name = CommonLanguageManager.Instance.widgets_quickWorld.CurrentValue(),
                Description = CommonLanguageManager.Instance.widgets_quickWorldDescription.CurrentValue(),
                Category = WidgetCategory.Game,
                DefaultSize = new WidgetCellSize(2, 1)
            }
            .AddPage<QuickWorldWidget1x1>(new WidgetCellSize(1, 1))
            .AddPage<QuickWorldWidget2x1>(new WidgetCellSize(2, 1)));

        Register(new WidgetDefinition
            {
                Kind = WidgetKind.QuickServer,
                Name = CommonLanguageManager.Instance.widgets_quickServer.CurrentValue(),
                Description = CommonLanguageManager.Instance.widgets_quickServerDescription.CurrentValue(),
                Category = WidgetCategory.Game,
                DefaultSize = new WidgetCellSize(2, 1)
            }
            .AddPage<QuickServerWidget1x1>(new WidgetCellSize(1, 1))
            .AddPage<QuickServerWidget2x1>(new WidgetCellSize(2, 1)));

        RegisterGameListWidget(WidgetKind.ServerList,
            CommonLanguageManager.Instance.widgets_serverList.CurrentValue(),
            CommonLanguageManager.Instance.widgets_serverListDescription.CurrentValue());
        var instanceListDefinition = new WidgetDefinition
        {
            Kind = WidgetKind.InstanceList,
            Name = CommonLanguageManager.Instance.widgets_instanceList.CurrentValue(),
            Description = CommonLanguageManager.Instance.widgets_instanceListDescription.CurrentValue(),
            Category = WidgetCategory.Game,
            DefaultSize = new WidgetCellSize(6, 3)
        };
        for (var columns = 3; columns <= 16; columns++)
        for (var rows = 3; rows <= 16; rows++)
        {
            var size = new WidgetCellSize(columns, rows);
            instanceListDefinition.AddPage(size, () => new InstanceListWidget(size));
        }
        Register(instanceListDefinition);
        RegisterGameListWidget(WidgetKind.WorldList,
            CommonLanguageManager.Instance.widgets_worldList.CurrentValue(),
            CommonLanguageManager.Instance.widgets_worldListDescription.CurrentValue());
        RegisterGameListWidget(WidgetKind.RecentPlayList,
            CommonLanguageManager.Instance.widgets_recentPlayList.CurrentValue(),
            CommonLanguageManager.Instance.widgets_recentPlayListDescription.CurrentValue());
        RegisterGameListWidget(WidgetKind.RecentInstanceList,
            CommonLanguageManager.Instance.widgets_recentInstanceList.CurrentValue(),
            CommonLanguageManager.Instance.widgets_recentInstanceListDescription.CurrentValue());
        RegisterGameListWidget(WidgetKind.FixedList,
            CommonLanguageManager.Instance.widgets_fixedList.CurrentValue(),
            CommonLanguageManager.Instance.widgets_fixedListDescription.CurrentValue());

        RegisterContinueWidget(WidgetKind.ContinuePlay,
            CommonLanguageManager.Instance.widgets_continuePlay.CurrentValue(),
            CommonLanguageManager.Instance.widgets_continuePlayDescription.CurrentValue(),
            () => new ContinuePlayWidget());
        RegisterContinueWidget(WidgetKind.ContinueInstance,
            CommonLanguageManager.Instance.widgets_continueInstance.CurrentValue(),
            CommonLanguageManager.Instance.widgets_continueInstanceDescription.CurrentValue(),
            () => new ContinueInstanceWidget());

        Register(new WidgetDefinition
            {
                Kind = WidgetKind.PlayTime,
                Name = CommonLanguageManager.Instance.widgets_playTime.CurrentValue(),
                Description = CommonLanguageManager.Instance.widgets_playTimeDescription.CurrentValue(),
                Category = WidgetCategory.Game,
                DefaultSize = new WidgetCellSize(2, 1)
            }
            .AddPage(new WidgetCellSize(2, 1), () => new PlayTimeWidget(new WidgetCellSize(2, 1))));


        var newsDef = new WidgetDefinition
        {
            Kind = WidgetKind.News,
            Name = CommonLanguageManager.Instance.widgets_news.CurrentValue(),
            Description = CommonLanguageManager.Instance.widgets_newsDescription.CurrentValue(),
            Category = WidgetCategory.Game,
            DefaultSize = new WidgetCellSize(2, 2)
        };
        for (var cols = 2; cols <= 6; cols++)
        for (var rows = 1; rows <= 6; rows++)
        {
            var size = new WidgetCellSize(cols, rows);
            newsDef.AddPage(size, () => new NewsWidget(size));
        }

        Register(newsDef);


        RegisterResourceWidget(WidgetKind.CpuResource, CommonLanguageManager.Instance.widgets_cpu.CurrentValue(),
            CommonLanguageManager.Instance.widgets_cpuDescription.CurrentValue(),
            size => new CpuResourceWidget(size));
        RegisterResourceWidget(WidgetKind.MemoryResource, CommonLanguageManager.Instance.widgets_memory.CurrentValue(),
            CommonLanguageManager.Instance.widgets_memoryDescription.CurrentValue(),
            size => new MemoryResourceWidget(size));
        RegisterResourceWidget(WidgetKind.DiskResource, CommonLanguageManager.Instance.widgets_disk.CurrentValue(),
            CommonLanguageManager.Instance.widgets_diskDescription.CurrentValue(),
            size => new DiskResourceWidget(size));
        RegisterResourceWidget(WidgetKind.NetworkResource, CommonLanguageManager.Instance.widgets_network.CurrentValue(),
            CommonLanguageManager.Instance.widgets_networkDescription.CurrentValue(),
            size => new NetworkResourceWidget(size));
        RegisterResourceWidget(WidgetKind.GpuResource, CommonLanguageManager.Instance.widgets_gpu.CurrentValue(),
            CommonLanguageManager.Instance.widgets_gpuDescription.CurrentValue(),
            size => new GpuResourceWidget(size));
    }

    private static void RegisterResourceWidget(
        WidgetKind kind, string name, string description,
        Func<WidgetCellSize, IWidgetContent> factory)
    {
        var def = new WidgetDefinition
        {
            Kind = kind,
            Name = name,
            Description = description,
            Category = WidgetCategory.Resource,
            DefaultSize = new WidgetCellSize(1, 1)
        };
        foreach (var size in new[]
                 {
                     new WidgetCellSize(1, 1),
                     new WidgetCellSize(2, 1),
                     new WidgetCellSize(2, 2)
                 })
            def.AddPage(size, () => factory(size));
        Register(def);
    }

    private static void RegisterGameListWidget(WidgetKind kind, string name, string description)
    {
        var definition = new WidgetDefinition
        {
            Kind = kind,
            Name = name,
            Description = description,
            Category = WidgetCategory.Game,
            DefaultSize = new WidgetCellSize(2, 2)
        };
        for (var columns = 2; columns <= 16; columns++)
        for (var rows = 2; rows <= 16; rows++)
        {
            var size = new WidgetCellSize(columns, rows);
            definition.AddPage(size, () => new GameListWidget(size));
        }

        Register(definition);
    }

    private static void RegisterContinueWidget(
        WidgetKind kind, string name, string description, Func<IWidgetContent> factory)
    {
        var size = new WidgetCellSize(2, 1);
        Register(new WidgetDefinition
        {
            Kind = kind,
            Name = name,
            Description = description,
            Category = WidgetCategory.Game,
            DefaultSize = size
        }.AddPage(size, factory));
    }
}
