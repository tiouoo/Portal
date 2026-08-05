using System.Text;
using XUserLauncher.Core;

namespace Portal.Bedrock;

internal sealed class PortalXUserLauncher(string instancePath)
{
    private readonly string _instancePath = Path.GetFullPath(instancePath);

    public void DeployHook()
    {
        const string resourceName = "XUserLauncher.Core.Dlls.XUserHook.dll";
        var preloadDirectory = Path.Combine(_instancePath, "preload");
        Directory.CreateDirectory(preloadDirectory);
        var hookPath = Path.Combine(preloadDirectory, "XUserHook.dll");
        using var input = typeof(XboxAuthClient).Assembly.GetManifestResourceStream(resourceName)
                          ?? throw new InvalidOperationException("XUserLauncher.Core 未包含 XUserHook.dll。 ");
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        var hook = buffer.ToArray();
        ReplaceHookValue(hook, Encoding.Unicode.GetBytes(@"\\.\pipe\BedrockBoot.XUser"),
            Encoding.Unicode.GetBytes(@"\\.\pipe\Portal.XUser"));
        ReplaceHookValue(hook, "BRBOOTX1"u8.ToArray(), "PORTALX1"u8.ToArray());
        File.WriteAllBytes(hookPath, hook);
    }

    public static async Task<XboxPreauth> AuthenticateAsync(string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidDataException("基岩账户缺少 Microsoft access token。");

        var identity = DeviceIdentity.Create();
        try
        {
            using var client = new XboxAuthClient();
            var preauth = await client.AuthenticateAsync(accessToken, identity, cancellationToken)
                .ConfigureAwait(false);
            preauth.CreateSessionPayload();
            return preauth;
        }
        catch
        {
            identity.Dispose();
            throw;
        }
    }

    public static async Task<SuspendedProcess> LaunchAndInjectAsync(string executable, string? arguments,
        string workingDirectory, XboxPreauth preauth, TimeSpan timeout)
    {
        var process = SuspendedProcess.Start(executable, arguments, workingDirectory);
        try
        {
            var injectTask = InjectAsync((int)process.ProcessId, preauth, timeout);
            process.Resume();
            await injectTask.ConfigureAwait(false);
            return process;
        }
        catch
        {
            process.Terminate();
            process.Dispose();
            throw;
        }
    }

    public static async Task InjectAsync(int processId, XboxPreauth preauth, TimeSpan timeout)
    {
        using var pipeServer = new PortalXUserPipeServer(processId, preauth.CreateSessionPayload(), timeout);
        await pipeServer.ServeAsync().ConfigureAwait(false);
    }

    private static void ReplaceHookValue(byte[] data, byte[] original, byte[] replacement)
    {
        if (replacement.Length > original.Length)
            throw new InvalidOperationException("Portal XUserHook 替换值长度超出原始空间。");
        var index = data.AsSpan().IndexOf(original);
        if (index < 0)
            throw new InvalidOperationException("XUserHook 协议与 Portal 支持的版本不匹配。");
        data.AsSpan(index, original.Length).Clear();
        replacement.CopyTo(data, index);
    }
}
