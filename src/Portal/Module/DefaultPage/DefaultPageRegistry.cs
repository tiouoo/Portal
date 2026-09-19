using System.Reflection;
using Portal.Core.Classes.Config;
using Portal.Localization;

namespace Portal.Module.DefaultPage;

public static class DefaultPageRegistry
{
    public static IReadOnlyList<DefaultPageEntry> Pages { get; } = Assembly.GetExecutingAssembly()
        .GetTypes()
        .Select(type => new { Type = type, Attribute = type.GetCustomAttribute<DefaultPageAttribute>() })
        .Where(item => item.Attribute != null)
        .OrderBy(item => item.Attribute!.Title)
        .Select(item => new DefaultPageEntry(item.Attribute!.Title,
            LocalizationService.ResolveKey(item.Attribute.Title), item.Type))
        .ToList();

    public static DefaultPageEntry Resolve(string? id)
    {
        return Pages.FirstOrDefault(item => item.Id == id)
               ?? Pages.First(item => item.Id == ConfigEntry.DefaultPageId);
    }

    public sealed record DefaultPageEntry(string Id, string Title, Type PageType);
}
