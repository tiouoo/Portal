using Portal.Localization;

namespace Portal.Core.Minecraft;

public sealed class MissingJavaVersionException : Exception
{
    public MissingJavaVersionException(int majorVersion)
        : base(string.Format(CommonLanguageManager.Instance.launch_missingJavaVersion.CurrentValue(), majorVersion))
    {
        MajorVersion = majorVersion;
    }

    public int MajorVersion { get; }
}
