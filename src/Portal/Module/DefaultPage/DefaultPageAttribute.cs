using System;

namespace Portal.Module.DefaultPage;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DefaultPageAttribute(string title) : Attribute
{
    public string Title { get; } = title;
}
