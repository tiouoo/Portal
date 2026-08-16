namespace Portal.Core.Module.AggregatedSearch;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AggregatedSearchPageAttribute : Attribute
{
    public AggregatedSearchPageAttribute(string title, string path, string iconKey)
    {
        Title = title;
        Path = path;
        IconKey = iconKey;
    }

    public string Title { get; }
    public string Path { get; }
    public string IconKey { get; }
}