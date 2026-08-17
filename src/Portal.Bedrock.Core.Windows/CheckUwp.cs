using System;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace Portal.Bedrock.Core.Windows;

public static class CheckUwp
{
	public static bool IsUwpPackageInstalled(string packageFamilyName)
	{
		if (string.IsNullOrWhiteSpace(packageFamilyName))
		{
			throw new ArgumentException("PackageFamilyName can't be empty", "packageFamilyName");
		}
		foreach (Package item in new PackageManager().FindPackagesForUser(string.Empty))
		{
			if (item.Id.FamilyName.Equals(packageFamilyName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}
}
