using Portal.Bedrock.Xbox;

namespace Portal.Bedrock;

internal sealed class PortalXUserLauncher(string instancePath)
{
    private const string HookResourceName = "Portal.Bedrock.XUserHook.dll";
    private readonly string _instancePath = Path.GetFullPath(instancePath);

    public void DeployHook()
    {
        var preloadDirectory = Path.Combine(_instancePath, "preload");
        Directory.CreateDirectory(preloadDirectory);
        var hookPath = Path.Combine(preloadDirectory, "XUserHook.dll");
        using var input = typeof(PortalXUserLauncher).Assembly.GetManifestResourceStream(HookResourceName)
                          ?? throw new InvalidOperationException("Portal.Bedrock.Windows 未包含 XUserHook.dll。");
        using var output = new FileStream(hookPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        input.CopyTo(output);
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
        string workingDirectory, XboxPreauth preauth, TimeSpan timeout, Action<uint>? processStarted = null)
    {
        var process = SuspendedProcess.Start(executable, arguments, workingDirectory);
        try
        {
            var injectTask = InjectAsync((int)process.ProcessId, preauth, timeout);
            process.Resume();
            processStarted?.Invoke(process.ProcessId);
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
        using var pipeServer = new XUserPipeServer(processId, preauth.CreateSessionPayload(), timeout);
        await pipeServer.StartAsync().ConfigureAwait(false);
    }
}
