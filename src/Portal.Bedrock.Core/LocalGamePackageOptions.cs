using System;
using System.Threading;

namespace Portal.Bedrock.Core;

public class LocalGamePackageOptions
{
	public required string FileFullPath;

	public required MinecraftBuildTypeVersion Type;

	public required string InstallDstFolder;

	public IProgress<DecompressProgress>? ExtractionProgress;

	public IProgress<InstallStates>? InstallStates;

	public CancellationToken? CancellationToken;

	public required MinecraftGameTypeVersion GameTypeVersion;

	public BackGroundConfig? BackGroundConfig;

	public string? GameName;

	public bool UseHardwareDecode { get; set; } = true;
}
