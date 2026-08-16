using System.Reflection;
using Newtonsoft.Json;
using Portal.Core.Classes.Entries;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.App.Service;

public sealed class AppVersionService
{
    private static AppVersionService? _instance;

    public static AppVersionService Instance => _instance ??= new AppVersionService();

    private const string VersionResourceName = "Portal.Core.Assets.version-ci.txt";

    public CiVersionInfo Version { get; private set; }

    private AppVersionService()
    {
        Version = LoadVersionInfo();
    }

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
        var versionInfo = JsonConvert.DeserializeObject<CiVersionInfo>(reader.ReadToEnd()) ?? CreateLocalVersionInfo();
        Logger.Info($"应用版本信息加载完成：{versionInfo.VersionTitle} ({versionInfo.Type})。");
        return versionInfo;
    }

    private static CiVersionInfo CreateLocalVersionInfo() => new()
    {
        Type = "dev",
        VersionTitle = "local-build"
    };
}
