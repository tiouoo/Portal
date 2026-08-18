using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Portal.Core.Minecraft.Instance.Bedrock;

public class JsonConfigEntry<T> where T : class, new()
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly JsonTypeInfo<T>? _typeInfo;

    public JsonConfigEntry(string configFile, bool isSave = true, JsonTypeInfo<T>? typeInfo = default)
    {
        Path = configFile;
        _typeInfo = typeInfo;
        IsSave = isSave;
        Load();
    }

    public T Data { get; set; }
    public string Path { get; }
    public bool IsSave { get; set; } = true;

    public void Load()
    {
        if (!File.Exists(Path))
        {
            Data = new T();
            Save();
            return;
        }

        var json = File.ReadAllText(Path);
        if (string.IsNullOrEmpty(json))
        {
            Data = new T();
            Save();
            return;
        }

        try
        {
            Data = _typeInfo != null
                ? JsonSerializer.Deserialize<T>(json, _typeInfo)
                : JsonSerializer.Deserialize<T>(json, Options);
        }
        catch
        {
            TryBackupCorruptedFile();
            Data = new T();
            Save();
        }

        Data ??= new T();
    }

    public void Save()
    {
        if (!IsSave)
            return;

        Data ??= new T();
        EnsureDirectoryExists();

        var json = _typeInfo != null
            ? JsonSerializer.Serialize(Data, _typeInfo)
            : JsonSerializer.Serialize(Data, Options);
        File.WriteAllText(Path, json);
    }

    private void EnsureDirectoryExists()
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }

    private void TryBackupCorruptedFile()
    {
        try
        {
            File.Copy(Path, Path + ".bak", true);
        }
        catch
        {
        }
    }
}
