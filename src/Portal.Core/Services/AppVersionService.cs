using System.Reflection;
using System.Text.Json;
using Portal.Core.Classes.Entries;
using Portal.Core.Json;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Services;

public sealed class AppVersionService
{
    private const string VersionResourceName = "Portal.Core.Assets.version-ci.txt";
    private static AppVersionService? _instance;

    private AppVersionService()
    {
        Version = LoadVersionInfo();
    }

    public static AppVersionService Instance => _instance ??= new AppVersionService();

    public CiVersionInfo Version { get; private set; }

    private static CiVersionInfo LoadVersionInfo()
    {
        Logger.Info("正在加载应用版本信息。");
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(VersionResourceName);
        if (stream is null)
        {
            Logger.Warning("未找到内嵌版本信息，使用本地开发版本信息。");
            return CreateLocalVersionInfo();
        }

        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        var versionInfo = string.IsNullOrWhiteSpace(text)
            ? null
            : JsonSerializer.Deserialize<CiVersionInfo>(text, PortalJson.Options);
        Logger.Info($"应用版本信息加载完成：{versionInfo?.VersionTitle ?? "local-build"} ({versionInfo?.Type ?? "dev"})。");
        return versionInfo ?? CreateLocalVersionInfo();
    }

    private static CiVersionInfo CreateLocalVersionInfo()
    {
        return new CiVersionInfo
        {
            Type = "dev",
            VersionTitle = "local-build"
        };
    }
}