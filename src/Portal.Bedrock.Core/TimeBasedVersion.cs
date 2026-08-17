using System;

namespace Portal.Bedrock.Core;

public static class TimeBasedVersion
{
	public static string GetVersion()
	{
		DateTime now = DateTime.Now;
		return $"{now.Year}.{now.Month}.{now.Day}.{now.Hour * 60 + now.Minute}";
	}

	public static Version GetVersionObject()
	{
		DateTime now = DateTime.Now;
		return new Version(now.Year, now.Month, now.Day, now.Hour * 60 + now.Minute);
	}
}
