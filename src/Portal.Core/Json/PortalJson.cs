using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Avalonia.Media;
using Portal.Core.Classes.Entries;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Json;

public static class PortalJson
{
    private static readonly Type[] WidgetDataTypes =
    [
        typeof(InstanceWidgetData), typeof(QuickWorldWidgetData), typeof(QuickServerWidgetData),
        typeof(MemoryWidgetData), typeof(ImageWidgetData), typeof(NewsWidgetData)
    ];

    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IndentSize = 2,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { ConfigureWidgetPolymorphism, ConfigureInstanceConfigShouldSerialize, ConfigureGetterOnlyCollections }
            }
        };
        options.Converters.Add(new LenientEnumConverterFactory());
        options.Converters.Add(new AvaloniaColorJsonConverter());
        return options;
    }

    private static void ConfigureWidgetPolymorphism(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(WidgetData)) return;

        var options = new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "$type",
            IgnoreUnrecognizedTypeDiscriminators = true
        };
        foreach (var type in WidgetDataTypes)
            options.DerivedTypes.Add(new JsonDerivedType(type, $"{type.FullName}, {type.Assembly.GetName().Name}"));
        typeInfo.PolymorphismOptions = options;
    }

    private static void ConfigureInstanceConfigShouldSerialize(JsonTypeInfo typeInfo)
    {
        if (!typeof(MinecraftInstanceConfig).IsAssignableFrom(typeInfo.Type)) return;

        foreach (var property in typeInfo.Properties)
        {
            if (property.Name == nameof(MinecraftInstanceConfig.RecentPlayFavorites))
                property.ShouldSerialize = (_, value) => (value as Dictionary<string, bool>)?.Count > 0;
            else if (property.Name == nameof(MinecraftInstanceConfig.PlayTimeByDate))
                property.ShouldSerialize = (_, value) => (value as Dictionary<string, long>)?.Count > 0;
        }
    }

    private static void ConfigureGetterOnlyCollections(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object) return;

        foreach (var property in typeInfo.Properties)
        {
            if (property.Set is not null || property.Get is null) continue;
            if (!IsPopulatableCollection(property.PropertyType)) continue;

            property.ObjectCreationHandling = JsonObjectCreationHandling.Populate;
        }
    }

    private static bool IsPopulatableCollection(Type type)
    {
        if (type == typeof(string) || type.IsArray) return false;

        if (typeof(ICollection).IsAssignableFrom(type) || typeof(IDictionary).IsAssignableFrom(type))
            return true;

        return HasPopulatableContract(type) || type.GetInterfaces().Any(HasPopulatableContract);
    }

    private static bool HasPopulatableContract(Type type)
    {
        if (!type.IsGenericType) return false;

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(ICollection<>)
               || definition == typeof(IDictionary<,>)
               || definition == typeof(IList<>);
    }
}

public sealed class LenientEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return (JsonConverter)Activator.CreateInstance(
            typeof(LenientEnumConverter<>).MakeGenericType(typeToConvert))!;
    }

    private sealed class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    return (T)Enum.ToObject(typeToConvert, reader.GetInt64());
                case JsonTokenType.String:
                    var text = reader.GetString();
                    if (Enum.TryParse<T>(text, true, out var parsed)) return parsed;
                    if (long.TryParse(text, out var numeric)) return (T)Enum.ToObject(typeToConvert, numeric);
                    return default;
                default:
                    throw new JsonException($"Cannot convert token {reader.TokenType} to enum {typeToConvert.Name}.");
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(Convert.ToInt64(value));
        }
    }
}

public sealed class AvaloniaColorJsonConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            if (reader.TokenType is JsonTokenType.Null) return default;
            if (reader.TokenType != JsonTokenType.StartObject) return default;

            var a = (byte)255;
            var r = (byte)0;
            var g = (byte)0;
            var b = (byte)0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType != JsonTokenType.PropertyName) continue;

                var name = reader.GetString();
                reader.Read();
                switch (name)
                {
                    case "A": a = reader.GetByte(); break;
                    case "R": r = reader.GetByte(); break;
                    case "G": g = reader.GetByte(); break;
                    case "B": b = reader.GetByte(); break;
                }
            }

            return new Color(a, r, g, b);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("A", value.A);
        writer.WriteNumber("R", value.R);
        writer.WriteNumber("G", value.G);
        writer.WriteNumber("B", value.B);
        writer.WriteEndObject();
    }
}
