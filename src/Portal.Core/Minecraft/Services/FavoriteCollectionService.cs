using System.Text.Json;
using Portal.Core.Const;
using Portal.Core.Json;
using Portal.Core.Minecraft.Models;
using Portal.Localization;

namespace Portal.Core.Minecraft.Services;

public enum FavoriteEdition
{
    Java,
    Bedrock
}

public class FavoriteResource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public FavoriteEdition Edition { get; set; }
    public ResourceKind Kind { get; set; }
    public ModDetailsSource Source { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
}

public sealed class FavoriteCollection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = CommonLanguageManager.Instance.favorite_defaultCollectionName.CurrentValue();
    public List<FavoriteResource> Items { get; set; } = [];
}

public sealed class FavoriteCollectionDocument
{
    public int Version { get; set; } = 1;
    public List<FavoriteCollection> Collections { get; set; } = [];
}

public sealed class FavoriteCollectionService
{
    private const string FileName = "Favorites.portal.json";
    private readonly string _path = Path.Combine(ConfigPath.UserDataRootPath, FileName);

    private FavoriteCollectionService()
    {
        Document = Load();
        EnsureCollection();
    }

    public static FavoriteCollectionService Instance { get; } = new();
    public FavoriteCollectionDocument Document { get; }

    public event EventHandler? Changed;

    public bool Contains(FavoriteResource resource)
    {
        return Document.Collections
            .SelectMany(collection => collection.Items)
            .Any(item => item.ProjectId == resource.ProjectId && item.Kind == resource.Kind &&
                         item.Source == resource.Source);
    }

    public void Add(FavoriteResource resource, string? collectionId = null)
    {
        var collection = Document.Collections.FirstOrDefault(item => item.Id == collectionId) ??
                         Document.Collections[0];
        if (collection.Items.Any(item =>
                item.ProjectId == resource.ProjectId && item.Kind == resource.Kind && item.Source == resource.Source))
            return;
        collection.Items.Add(resource);
        Save();
    }

    public void Remove(FavoriteResource resource)
    {
        foreach (var collection in Document.Collections)
            collection.Items.RemoveAll(item =>
                item.ProjectId == resource.ProjectId && item.Kind == resource.Kind && item.Source == resource.Source);
        Save();
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigPath.UserDataRootPath);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Document, PortalJson.Options));
        File.Move(temporaryPath, _path, true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Import(string path)
    {
        var imported = JsonSerializer.Deserialize<FavoriteCollectionDocument>(File.ReadAllText(path), PortalJson.Options);
        if (imported?.Collections is null)
            throw new InvalidDataException(CommonLanguageManager.Instance.favorite_invalidFileFormat.CurrentValue());
        foreach (var collection in
                 imported.Collections.Where(collection => !string.IsNullOrWhiteSpace(collection.Name)))
        {
            collection.Id = Guid.NewGuid().ToString("N");
            collection.Items ??= [];
            Document.Collections.Add(collection);
        }

        EnsureCollection();
        Save();
    }

    public void Export(FavoriteCollection collection, string path)
    {
        var document = new FavoriteCollectionDocument { Collections = [collection] };
        File.WriteAllText(path, JsonSerializer.Serialize(document, PortalJson.Options));
    }

    private FavoriteCollectionDocument Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<FavoriteCollectionDocument>(File.ReadAllText(_path), PortalJson.Options) ??
                  new FavoriteCollectionDocument()
                : new FavoriteCollectionDocument();
        }
        catch
        {
            return new FavoriteCollectionDocument();
        }
    }

    private void EnsureCollection()
    {
        if (Document.Collections.Count == 0)
            Document.Collections.Add(new FavoriteCollection());
    }
}
