using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Portal.Core.Entity;

public class ConfigEntity<T> where T : new()
{
    public static JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly JsonTypeInfo<T>? TypeInfo;

    public ConfigEntity(string configFile, bool isSave = true, JsonTypeInfo<T>? typeInfo = default)
    {
        Path = configFile;
        TypeInfo = typeInfo;
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
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            Save();
            return;
        }

        var json = File.ReadAllText(Path);
        if (string.IsNullOrEmpty(json))
            Save();
        else
            try
            {
                Data = TypeInfo != null
                    ? JsonSerializer.Deserialize<T>(json, TypeInfo)
                    : JsonSerializer.Deserialize<T>(json, Options);
            }
            catch
            {
                try
                {
                    File.Copy(Path, Path + ".bak", true);
                }
                catch
                {
                }

                Save();
            }


        Data ??= new T();
    }

    public void Save()
    {
        if (!IsSave)
            return;

        if (Data == null) Data = new T();

        var jsresult = TypeInfo != null
            ? JsonSerializer.Serialize(Data, TypeInfo)
            : JsonSerializer.Serialize(Data, Options);
        File.WriteAllText(Path, jsresult);
    }
}