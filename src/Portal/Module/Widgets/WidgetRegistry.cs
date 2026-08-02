using Portal.Classes.Entries;
using Portal.Views.Widgets;

namespace Portal.Module.Widgets;

public sealed class WidgetDefinition
{
    public WidgetKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public WidgetCellSize DefaultSize { get; init; } = new(1, 1);

    private readonly List<(WidgetCellSize Size, Func<IWidgetContent> Factory)> _pages = [];

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
        WidgetCellSize best = _pages[0].Size;
        double bestDistance = double.MaxValue;
        foreach (var (size, _) in _pages)
        {
            var dims = WidgetGeometry.GetSize(size);
            double distance = Math.Sqrt(Math.Pow(dims.Width - width, 2) + Math.Pow(dims.Height - height, 2));
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

    public static IEnumerable<WidgetDefinition> Definitions
    {
        get
        {
            EnsureInitialized();
            return _definitions.Values;
        }
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
            DefaultSize = new WidgetCellSize(1, 1)
        }
            .AddPage<Clock1x1>(new WidgetCellSize(1, 1))
            .AddPage<Clock2x1>(new WidgetCellSize(2, 1)));

        Register(new WidgetDefinition
        {
            Kind = WidgetKind.Instance,
            Name = "实例",
            Description = "快速查看与启动实例",
            DefaultSize = new WidgetCellSize(2, 1)
        }.AddPage<InstanceWidget2x1>(new WidgetCellSize(2, 1)));

        Register(new WidgetDefinition
        {
            Kind = WidgetKind.QuickWorld,
            Name = "快速进入世界",
            Description = "选择实例与存档，一键进入",
            DefaultSize = new WidgetCellSize(2, 1)
        }.AddPage<QuickWorldWidget2x1>(new WidgetCellSize(2, 1)));

        Register(new WidgetDefinition
        {
            Kind = WidgetKind.QuickServer,
            Name = "快速进入服务器",
            Description = "选择实例并输入地址，一键进入",
            DefaultSize = new WidgetCellSize(2, 1)
        }.AddPage<QuickServerWidget2x1>(new WidgetCellSize(2, 1)));
    }
}
