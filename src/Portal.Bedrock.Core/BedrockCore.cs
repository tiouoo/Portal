using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Portal.Localization;

namespace Portal.Bedrock.Core;

public class BedrockCore
{
	public virtual async Task<InstallResult?> InstallPackageAsync(LocalGamePackageOptions options)
	{
		ArgumentNullException.ThrowIfNull(options, "options");
		Directory.CreateDirectory(options.InstallDstFolder);
		if (options.Type == MinecraftBuildTypeVersion.GDK)
		{
			byte[] cik = ParseCik(options.Cik);

			await Task.Run(async delegate
			{
				using MsiXVDDecoder decoder = new MsiXVDDecoder(new CikKey(cik), options.UseHardwareDecode);
				using MsiXVDStream stream = new MsiXVDStream(options.FileFullPath);
				stream.Parse();
				options.InstallStates?.Report(InstallStates.Extracting);
				await stream.ExtractTaskAsync(Path.GetFullPath(options.InstallDstFolder), decoder, options.ExtractionProgress, options.CancellationToken ?? CancellationToken.None);
				options.InstallStates?.Report(InstallStates.Extracted);
			}, options.CancellationToken ?? CancellationToken.None);
			return new InstallResult();
		}
		throw new PlatformNotSupportedException(CommonLanguageManager.Instance.bedrockInstall_uwpWindowsOnly.CurrentValue());
	}

	private static byte[] ParseCik(string? value)
	{
		if (!string.IsNullOrEmpty(value))
		{
			try
			{
				byte[] cik = Convert.FromHexString(value.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase)
					.Replace(" ", "", StringComparison.Ordinal));
				if (cik.Length == 48) return cik;
			}
			catch (FormatException)
			{
			}
		}

		throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockInstall_gdkCikMissing.CurrentValue());
	}

	public async Task<string> GetPackageUri(BuildInfo buildInfo, Architecture devicesArch)
	{
		Variation variation = buildInfo.Variations.FirstOrDefault((Variation variation2) => variation2.Arch == devicesArch) ?? throw new BedrockCoreException($"Unable to find {devicesArch} Version");
		if (variation.MetaData.Count == 0)
		{
			throw new BedrockCoreNoAvailbaleVersionUri("There is no available Uri to download");
		}
		string metadata = variation.MetaData.Last();
		if (metadata.StartsWith("http", StringComparison.OrdinalIgnoreCase))
		{
			return metadata;
		}
		try
		{
			string uri = await UpdateIDHelper.GetUriAsync(metadata);
			if (string.IsNullOrEmpty(uri))
			{
				throw new BedrockCoreNoAvailbaleVersionUri("There is no available uri for this");
			}
			return uri;
		}
		catch (BedrockCoreException)
		{
			throw;
		}
	}
}
