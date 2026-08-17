namespace Portal.Bedrock.Core;

public struct DecompressProgress
{
	public string FileName;

	public long CurrentCount;

	public long TotalCount;

	public readonly double Percentage => (TotalCount <= 0) ? 0.0 : ((double)CurrentCount / (double)TotalCount * 100.0);
}
