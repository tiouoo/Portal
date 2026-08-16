using Portal.Core.Module.Widgets;
using Portal.Views.Widgets;

namespace Portal.Module.Widgets;

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
            throw new InvalidOperationException($"组件类型 {definition.Kind} 已注册");
        _definitions[definition.Kind] = definition;
    }

    public static WidgetDefinition? Get(WidgetKind kind)
    {
        EnsureInitialized();
        return _definitions.GetValueOrDefault(kind);
    }

    public static IWidgetContent Create(WidgetKind kind, WidgetCellSize size)
    {
        var definition = Get(kind) ?? throw new InvalidOperationException($"组件类型 {kind} 未注册");
        return definition.Create(size);
    }

    private static void RegisterBuiltins()
    {
        Register(new WidgetDefinition
            {
                Kind = WidgetKind.Clock,
                Name = "时钟",
                Description = "时间与日期",
                Category = WidgetCategory.Utility,
                DefaultSize = new WidgetCellSize(1, 1)
            }
            .AddPage<Clock1x1>(new WidgetCellSize(1, 1))
            .AddPage<Clock2x1>(new WidgetCellSize(2, 1)));


        var imageDef = new WidgetDefinition
        {
            Kind = WidgetKind.Image,
            Name = "图片",
            Description = "显示一张本地图片，完全占满组件",
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
            Name = "搜索框",
            Description = "聚合搜索实例、存档、服务器、页面及下载站资源",
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
                Name = "实例",
                Description = "快速查看与启动实例",
                Category = WidgetCategory.Game,
                DefaultSize = new WidgetCellSize(2, 1)
            }
            .AddPage<InstanceWidget1x1>(new WidgetCellSize(1, 1))
            .AddPage<InstanceWidget2x1>(new WidgetCellSize(2, 1)));

        Register(new WidgetDefinition
            {
                Kind = WidgetKind.QuickWorld,
                Name = "快速进入世界",
                Description = "选择实例与存档，一键进入",
                Category = WidgetCategory.Game,
                DefaultSize = new WidgetCellSize(2, 1)
            }
            .AddPage<QuickWorldWidget1x1>(new WidgetCellSize(1, 1))
            .AddPage<QuickWorldWidget2x1>(new WidgetCellSize(2, 1)));

        Register(new WidgetDefinition
            {
                Kind = WidgetKind.QuickServer,
                Name = "快速进入服务器",
                Description = "选择实例并输入地址，一键进入",
                Category = WidgetCategory.Game,
                DefaultSize = new WidgetCellSize(2, 1)
            }
            .AddPage<QuickServerWidget1x1>(new WidgetCellSize(1, 1))
            .AddPage<QuickServerWidget2x1>(new WidgetCellSize(2, 1)));


        var newsDef = new WidgetDefinition
        {
            Kind = WidgetKind.News,
            Name = "新闻",
            Description = "展示最新 Minecraft 新闻（Java 版 / 基岩版）",
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


        RegisterResourceWidget(WidgetKind.CpuResource, "CPU 占用", "处理器使用率",
            size => new CpuResourceWidget(size));
        RegisterResourceWidget(WidgetKind.MemoryResource, "内存占用", "物理内存使用情况",
            size => new MemoryResourceWidget(size));
        RegisterResourceWidget(WidgetKind.DiskResource, "磁盘占用", "系统盘使用情况",
            size => new DiskResourceWidget(size));
        RegisterResourceWidget(WidgetKind.NetworkResource, "网络占用", "上下行传输速率",
            size => new NetworkResourceWidget(size));
        RegisterResourceWidget(WidgetKind.GpuResource, "GPU 占用", "显卡使用率",
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
}