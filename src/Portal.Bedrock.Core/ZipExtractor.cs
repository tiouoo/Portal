using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace Portal.Bedrock.Core;

public static class ZipExtractor
{
	public static async Task ExtractWithProgressAsync(string zipPath, string extractPath, IProgress<DecompressProgress>? progress, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!File.Exists(zipPath))
		{
			throw new FileNotFoundException("ZIP file not found", zipPath);
		}
		Directory.CreateDirectory(extractPath);
		int count;
		using (ZipArchive zipArchive = ZipFile.OpenRead(zipPath))
		{
			count = zipArchive.Entries.Count;
		}
		DecompressProgress decompressProgress = new DecompressProgress
		{
			TotalCount = count,
			CurrentCount = 0L,
			FileName = string.Empty
		};
		using ZipArchive archive = ZipFile.OpenRead(zipPath);
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!string.IsNullOrEmpty(entry.Name))
			{
				string fullPath = Path.GetFullPath(Path.Combine(extractPath, entry.FullName));
				if (!fullPath.StartsWith(extractPath, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException("ZIP file contains potential path traversal attacks.");
				}
				string directoryName = Path.GetDirectoryName(fullPath);
				if (!string.IsNullOrEmpty(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
				await using Stream sourceStream = entry.Open();
				await using FileStream targetStream = File.Create(fullPath);
				await sourceStream.CopyToAsync(targetStream, cancellationToken);
			}
			decompressProgress.CurrentCount++;
			decompressProgress.FileName = entry.FullName;
			progress?.Report(decompressProgress);
		}
	}
}
