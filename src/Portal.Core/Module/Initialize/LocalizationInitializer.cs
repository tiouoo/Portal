using System.Globalization;
using System.Text.Json;
using Portal.Core.Const;
using Portal.Localization;

namespace Portal.Core.Module.Initialize;

public static class LocalizationInitializer
{
    public static void Initialize()
    {
        LocalizationService.SetCulture(ResolveInitialCulture());
    }

    private static CultureInfo ResolveInitialCulture()
    {
        try
        {
            if (File.Exists(ConfigPath.SettingDataPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(ConfigPath.SettingDataPath));
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!string.Equals(property.Name, "Language", StringComparison.OrdinalIgnoreCase) ||
                        property.Value.ValueKind != JsonValueKind.String)
                        continue;
                    var name = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                        return new CultureInfo(name);
                    break;
                }
            }
        }
        catch
        {
        }

        return CultureInfo.CurrentUICulture;
    }
}
