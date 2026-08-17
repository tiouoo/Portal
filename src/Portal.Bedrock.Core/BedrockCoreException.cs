using System;

namespace Portal.Bedrock.Core;

public class BedrockCoreException : Exception
{
	public BedrockCoreException(string message)
		: base(message)
	{
	}

	public BedrockCoreException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
