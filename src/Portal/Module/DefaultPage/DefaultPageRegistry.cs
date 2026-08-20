using System.Reflection;
using Portal.Localization;

namespace Portal.Module.DefaultPage;

public static class DefaultPageRegistry
{
    public static IReadOnlyList<DefaultPageEntry> Pages { get; } = Assembly.GetExecutingAssembly()
        .GetTypes()
        .Select(type => new { Type = type, Attribute = type.GetCustomAttribute<DefaultPageAttribute>() })
        .Where(item => item.Attribute != null)
        .OrderBy(item => item.Attribute!.Title)
        .Select(item => new DefaultPageEntry(LocalizationService.ResolveKey(item.Attribute!.Title), item.Type))
        .ToList();

    public sealed record DefaultPageEntry(string Title, Type PageType);
}