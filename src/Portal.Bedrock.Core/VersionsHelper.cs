using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Portal.Bedrock.Core;

public static class VersionsHelper
{
	private static readonly object Lock = new object();

	public static string GetNextVersion(Version currentVersion)
	{
		lock (Lock)
		{
			long ticks = DateTime.Now.Ticks;
			int seed = (int)(ticks & 0xFFFFFFFFu) ^ (int)(ticks >> 32) ^ Environment.TickCount;
			Random rand = new Random(seed);
			string sevenDigitStr = rand.Next(1000000, 10000000).ToString();
			int[] array = (from _ in Enumerable.Range(0, 7)
				orderby rand.Next()
				select _).Take(2).ToArray();
			Array.Sort(array);
			int num = int.Parse(string.Concat(sevenDigitStr.AsSpan(array[0], 1), sevenDigitStr.AsSpan(array[1], 1)));
			int num2 = int.Parse(currentVersion.Build.ToString().PadRight(5, '0').Substring(0, 3) + num.ToString().PadLeft(2, '0'));
			if (num2 > 65535)
			{
				num2 %= 65535;
			}
			if (num2 == 0)
			{
				num2 = 1;
			}
			int num3 = int.Parse(new string((from i in Enumerable.Range(0, 7).Except(array)
				orderby i
				select sevenDigitStr[i]).ToArray()));
			if (num3 > 65535)
			{
				num3 %= 65535;
			}
			if (num3 == 0)
			{
				num3 = 1;
			}
			return $"{currentVersion.Major}.{currentVersion.Minor}.{num2}.{num3}";
		}
	}

	public static async Task<BuildDatabase?> GetBuildDatabaseAsync(string httpAddress, CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			using HttpClient client = new HttpClient();
			client.DefaultRequestHeaders.UserAgent.ParseAdd("mcappx_developer");
			using HttpResponseMessage response = await client.GetAsync(httpAddress, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			response.EnsureSuccessStatusCode();
			BuildDatabase result;
			await using (Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken))
			{
				result = await JsonSerializer.DeserializeAsync<BuildDatabase>(stream, BedrockJsonOptions.Options, cancellationToken);
			}
			return result;
		}
		catch (Exception ex) when (!(ex is OperationCanceledException) || !cancellationToken.IsCancellationRequested)
		{
			throw new BedrockCoreException("Get BuildDataBase Error", ex);
		}
	}
}
