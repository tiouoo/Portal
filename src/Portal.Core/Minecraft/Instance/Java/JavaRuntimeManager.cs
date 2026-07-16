using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Utilities;

namespace Portal.Core.Minecraft.Instance.Java;

public static class JavaRuntimeManager
{
    public static async Task<JavaRuntimeEntry?> FromPathAsync(string javaPath, CancellationToken cancellationToken = default)
    {
        var java = await JavaUtil.GetJavaInfoAsync(javaPath, cancellationToken);
        return java == null ? null : Convert(java);
    }

    public static async Task<IReadOnlyList<JavaRuntimeEntry>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<JavaRuntimeEntry>();
        await foreach (var java in JavaUtil.EnumerableJavaAsync(cancellationToken))
        {
            if (java != null)
                result.Add(Convert(java));
        }

        return result;
    }

    private static JavaRuntimeEntry Convert(JavaEntry java)
    {
        return new JavaRuntimeEntry
        {
            JavaPath = java.JavaPath,
            JavaType = java.JavaType,
            JavaVersion = java.JavaVersion,
            MajorVersion = java.MajorVersion,
            Is64Bit = java.Is64bit
        };
    }
}
