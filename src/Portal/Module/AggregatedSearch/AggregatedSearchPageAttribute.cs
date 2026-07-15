using System;

namespace Portal.Module.AggregatedSearch;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class AggregatedSearchPageAttribute : Attribute
{
    public string Title { get; }
    public string Path { get; }
    public string IconKey { get; }

    public AggregatedSearchPageAttribute(string title, string path, string iconKey)
    {
        Title = title;
        Path = path;
        IconKey = iconKey;
    }
}
