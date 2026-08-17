using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Management.Deployment;

namespace Portal.Bedrock.Core.Windows;

public static class VCRuntimeHelper
{
	public static class VCUri
	{
		public static string Uwpx64 = "https://raw.gitcode.com/gcw_lJgzYtGB/RecycleObjects/blobs/3112f116e0cebdf5b1ead2da347f516406e2a365/Microsoft.VCLibs.140.00_14.0.33519.0_x64__8wekyb3d8bbwe.Appx";

		public static string Uwpx86 = "https://raw.gitcode.com/gcw_lJgzYtGB/RecycleObjects/blobs/f1cead0f80316261fd170c8f54f6cca99f4eaf22/Microsoft.VCLibs.140.00_14.0.33519.0_x86__8wekyb3d8bbwe.Appx";

		public static string Uwparm = "https://raw.gitcode.com/gcw_lJgzYtGB/RecycleObjects/blobs/106072935eb8232132813cec6c98b979544f69d6/Microsoft.VCLibs.140.00_14.0.33519.0_arm__8wekyb3d8bbwe.Appx";

		public static string Uwparm64 = "https://raw.gitcode.com/gcw_lJgzYtGB/RecycleObjects/blobs/90f5bd2c05a92f1ed5b60e1a5cc69be1627cff13/Microsoft.VCLibs.140.00_14.0.33519.0_arm64__8wekyb3d8bbwe.Appx";

		public static string Win32x64 = "https://gitcode.com/gcw_lJgzYtGB/RecycleObjects/releases/download/VCRuntime140GDK/VC_redist.x64.exe";

		public static string Win32x86 = "https://gitcode.com/gcw_lJgzYtGB/RecycleObjects/releases/download/VCRuntime140GDK/VC_redist.x86.exe";

		public static string Win32arm64 = "https://gitcode.com/gcw_lJgzYtGB/RecycleObjects/releases/download/VCRuntime140GDK/VC_redist.arm64.exe";

		public static string GameInputRedist = "https://raw.gitcode.com/gcw_lJgzYtGB/RecycleObjects/blobs/babbbbf96d352658f85ff0287e64bcd485b5f001/GameInputRedist.msi";
	}

	public static async Task CompleteVCRuntimeAsync(Architecture architecture)
	{
		try
		{
			string uri = architecture switch
			{
				Architecture.X86 => VCUri.Uwpx86, 
				Architecture.X64 => VCUri.Uwpx64, 
				Architecture.Arm64 => VCUri.Uwparm64, 
				_ => VCUri.Uwpx64, 
			};
			byte[] uwpVC = await DownloadPackageAsync(uri);
			uri = architecture switch
			{
				Architecture.X64 => VCUri.Win32x64, 
				Architecture.X86 => VCUri.Win32x86, 
				Architecture.Arm64 => VCUri.Win32arm64, 
				_ => VCUri.Win32x64, 
			};
			byte[] bytes = await DownloadPackageAsync(uri);
			string appxPath = Path.GetTempFileName() + ".appx";
			string exePath = Path.GetTempFileName() + ".exe";
			await File.WriteAllBytesAsync(appxPath, uwpVC);
			await File.WriteAllBytesAsync(exePath, bytes);
			await UwpRegister.AddAppxAsync(new DeploymentOptionsConfig
			{
				CancellationToken = new CancellationToken(canceled: false),
				DeploymentOptions = (DeploymentOptions)0,
				PackagePath = appxPath
			});
			using Process process = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = exePath,
					Arguments = "/install /quiet",
					UseShellExecute = false,
					CreateNoWindow = true,
					WindowStyle = ProcessWindowStyle.Hidden
				}
			};
			process.Start();
		}
		catch
		{
			throw new BedrockCoreException("Get VCPackage Error");
		}
	}

	public static async Task InstallGameInput()
	{
		try
		{
			byte[] bytes = await DownloadPackageAsync(VCUri.GameInputRedist);
			string fileName = Path.GetTempFileName() + ".msi";
			await File.WriteAllBytesAsync(fileName, bytes);
			MsiHelper.InstallMsiSilently(fileName);
		}
		catch
		{
			throw new BedrockCoreException("Install GameInputRedist Error");
		}
	}

	private static async Task<byte[]> DownloadPackageAsync(string uri)
	{
		using HttpClient client = new HttpClient();
		using HttpResponseMessage response = await client.GetAsync(uri);
		if (response.StatusCode != HttpStatusCode.OK)
		{
			throw new BedrockCoreNetWorkError("Get VCPackage Error");
		}
		return await response.Content.ReadAsByteArrayAsync();
	}
}
