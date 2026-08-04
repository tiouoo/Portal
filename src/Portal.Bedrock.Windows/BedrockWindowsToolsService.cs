using System.Diagnostics;
using System.Text;
using BedrockLauncher.Core;
using Portal.Bedrock.Standard.Interface;

namespace Portal.Bedrock;

public sealed class BedrockWindowsToolsService : IBedrockToolsService
{
    public async Task<bool> IsWindowsAppSdk18InstalledAsync(CancellationToken cancellationToken = default)
    {
        const string script = "& { $p = Get-AppxPackage | Where-Object { $_.Version -like '8000.*' -and $_.Name -like '*WinAppRuntime*' }; if (($p.Name -like '*Main*') -and ($p.Name -like '*Singleton*') -and ($p.Name -like '*DDLM*')) { 'TRUE' } else { 'FALSE' } }";
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 PowerShell。");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"检测 Windows App SDK 失败：{error.Trim()}");
        return output.Contains("TRUE", StringComparison.OrdinalIgnoreCase);
    }

    public async Task UninstallMinecraftAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var core = new BedrockCore();
        await core.RemoveUWPGameAsync(MinecraftGameTypeVersion.Release).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await core.RemoveUWPGameAsync(MinecraftGameTypeVersion.Preview).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await core.RemoveUWPGameAsync(MinecraftGameTypeVersion.Beta).ConfigureAwait(false);
    }
}
