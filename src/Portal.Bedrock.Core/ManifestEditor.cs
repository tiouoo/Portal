using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Portal.Bedrock.Core;

public static class ManifestEditor
{
	private const string SccdBase64 = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPEN1c3RvbUNhcGFiaWxpdHlEZXNjcmlwdG9yIHhtbG5zPSJodHRwOi8vc2NoZW1hcy5taWNyb3NvZnQuY29tL2FwcHgvMjAxOC9zY2NkIiB4bWxuczpzPSJodHRwOi8vc2NoZW1hcy5taWNyb3NvZnQuY29tL2FwcHgvMjAxOC9zY2NkIj4KICA8Q3VzdG9tQ2FwYWJpbGl0aWVzPgogICAgPEN1c3RvbUNhcGFiaWxpdHkgTmFtZT0iTWljcm9zb2Z0LmNvcmVBcHBBY3RpdmF0aW9uXzh3ZWt5YjNkOGJid2UiPjwvQ3VzdG9tQ2FwYWJpbGl0eT4KICA8L0N1c3RvbUNhcGFiaWxpdGllcz4KICA8QXV0aG9yaXplZEVudGl0aWVzIEFsbG93QW55PSJ0cnVlIi8+CiAgPENhdGFsb2c+RkZGRjwvQ2F0YWxvZz4KPC9DdXN0b21DYXBhYmlsaXR5RGVzY3JpcHRvcj4=";

	private const string ExtensionsBlock = " <Extensions>\n        <uap4:Extension Category=\"windows.loopbackAccessRules\">\n          <uap4:LoopbackAccessRules>\n            <uap4:Rule Direction=\"out\" PackageFamilyName=\"Microsoft.MEECC_8wekyb3d8bbwe\" />\n          </uap4:LoopbackAccessRules>\n        </uap4:Extension>\n        <uap:Extension Category=\"windows.fileTypeAssociation\" EntryPoint=\"App2\">\n          <uap:FileTypeAssociation Name=\"mcperf\">\n            <uap:DisplayName>MCPERF</uap:DisplayName>\n            <uap:InfoTip>Launch Minecraft and import world</uap:InfoTip>\n            <uap:SupportedFileTypes>\n              <uap:FileType>.MCPERF</uap:FileType>\n            </uap:SupportedFileTypes>\n          </uap:FileTypeAssociation>\n        </uap:Extension>\n        <uap:Extension Category=\"windows.fileTypeAssociation\" EntryPoint=\"App2\">\n          <uap:FileTypeAssociation Name=\"mcshortcut\">\n            <uap:DisplayName>MCSHORTCUT</uap:DisplayName>\n            <uap:InfoTip>Launch Minecraft and load world</uap:InfoTip>\n            <uap:SupportedFileTypes>\n              <uap:FileType>.MCSHORTCUT</uap:FileType>\n            </uap:SupportedFileTypes>\n          </uap:FileTypeAssociation>\n        </uap:Extension>\n        <uap:Extension Category=\"windows.fileTypeAssociation\" EntryPoint=\"App2\">\n          <uap:FileTypeAssociation Name=\"mcpack\">\n            <uap:DisplayName>MCPACK</uap:DisplayName>\n            <uap:InfoTip>Launch Minecraft and import resource pack</uap:InfoTip>\n            <uap:SupportedFileTypes>\n              <uap:FileType>.MCPACK</uap:FileType>\n            </uap:SupportedFileTypes>\n          </uap:FileTypeAssociation>\n        </uap:Extension>\n        <uap:Extension Category=\"windows.fileTypeAssociation\" EntryPoint=\"App2\">\n          <uap:FileTypeAssociation Name=\"mcworld\">\n            <uap:DisplayName>MCWORLD</uap:DisplayName>\n            <uap:InfoTip>Launch Minecraft and import world</uap:InfoTip>\n            <uap:SupportedFileTypes>\n              <uap:FileType>.MCWORLD</uap:FileType>\n            </uap:SupportedFileTypes>\n          </uap:FileTypeAssociation>\n        </uap:Extension>\n        <uap:Extension Category=\"windows.fileTypeAssociation\" EntryPoint=\"App2\">\n          <uap:FileTypeAssociation Name=\"mcproject\">\n            <uap:DisplayName>MCPROJECT</uap:DisplayName>\n            <uap:InfoTip>Launch Minecraft and import project</uap:InfoTip>\n            <uap:SupportedFileTypes>\n              <uap:FileType>.MCPROJECT</uap:FileType>\n            </uap:SupportedFileTypes>\n          </uap:FileTypeAssociation>\n        </uap:Extension>\n        <uap:Extension Category=\"windows.fileTypeAssociation\" EntryPoint=\"App2\">\n          <uap:FileTypeAssociation Name=\"mceditoraddon\">\n            <uap:DisplayName>MCEDITORADDON</uap:DisplayName>\n            <uap:InfoTip>Launch Minecraft and import editor addon</uap:InfoTip>\n            <uap:SupportedFileTypes>\n              <uap:FileType>.MCEDITORADDON</uap:FileType>\n            </uap:SupportedFileTypes>\n          </uap:FileTypeAssociation>\n        </uap:Extension>\n        <uap:Extension Category=\"windows.protocol\">\n          <uap:Protocol Name=\"ms-xbl-multiplayer\" />\n        </uap:Extension>\n        <uap:Extension Category=\"windows.protocol\">\n          <uap:Protocol Name=\"minecraft\" />\n        </uap:Extension>\n        <uap:Extension Category=\"windows.fileTypeAssociation\" EntryPoint=\"App2\">\n          <uap:FileTypeAssociation Name=\"mcaddon\">\n            <uap:DisplayName>MCADDON</uap:DisplayName>\n            <uap:InfoTip>Launch Minecraft and import addon</uap:InfoTip>\n            <uap:SupportedFileTypes>\n              <uap:FileType>.MCADDON</uap:FileType>\n            </uap:SupportedFileTypes>\n          </uap:FileTypeAssociation>\n        </uap:Extension>\n        <uap:Extension Category=\"windows.fileTypeAssociation\" EntryPoint=\"App2\">\n          <uap:FileTypeAssociation Name=\"mctemplate\">\n            <uap:DisplayName>MCTEMPLATE</uap:DisplayName>\n            <uap:InfoTip>Launch Minecraft and import world template</uap:InfoTip>\n            <uap:SupportedFileTypes>\n              <uap:FileType>.MCTEMPLATE</uap:FileType>\n            </uap:SupportedFileTypes>\n          </uap:FileTypeAssociation>\n        </uap:Extension>\n      </Extensions>";

	public static async Task<bool> EditManifest(string directory, string? gameName, BackGroundConfig? editer)
	{
		if (string.IsNullOrEmpty(directory))
		{
			throw new ArgumentNullException("directory");
		}
		string manifestPath = Path.Combine(directory, "AppxManifest.xml");
		if (!File.Exists(manifestPath))
		{
			return false;
		}
		try
		{
			return await Task.Run(delegate
			{
				XDocument xDocument = XDocument.Load(manifestPath, LoadOptions.PreserveWhitespace);
				XNamespace xNamespace = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
				XNamespace rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";
				XNamespace uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10/4";
				XNamespace uap2 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";
				XNamespace xNamespace2 = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
				XNamespace xNamespace3 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4";
				XElement root = xDocument.Root;
				if (root == null)
				{
					return false;
				}
				UpdateIgnorableNamespaces(root, rescap, uap, uap2, xNamespace3);
				UpdateApplicationTrustLevel(root, xNamespace, uap2);
				UpdateCapabilities(root, xNamespace, rescap, uap);
				XElement xElement = root.Element(xNamespace + "Applications")?.Element(xNamespace + "Application");
				XElement xElement2 = xElement?.Element(xNamespace + "Extensions");
				XElement xElement3 = root.Element(xNamespace + "Identity");
				string text = xElement3?.Attribute("Version")?.Value;
				if (!string.IsNullOrEmpty(text))
				{
					xElement3.SetAttributeValue("Version", VersionsHelper.GetNextVersion(new Version(text)));
				}
				xElement2?.RemoveAll();
				xElement.SetAttributeValue(xNamespace3 + "SupportsMultipleInstances", "true");
				XElement xElement4 = xElement.Element(xNamespace2 + "VisualElements");
				if (xElement4 != null)
				{
					if (!string.IsNullOrEmpty(gameName))
					{
						xElement4.SetAttributeValue("DisplayName", gameName);
					}
					xElement4.SetAttributeValue("AppListEntry", "none");
					if (editer.HasValue)
					{
						XElement xElement5 = xElement4.Element(xNamespace2 + "SplashScreen");
						if (xElement5 != null)
						{
							if (!string.IsNullOrEmpty(editer.Value.FileFullPath))
							{
								string fileName = Path.GetFileName(editer.Value.FileFullPath);
								File.Copy(editer.Value.FileFullPath, Path.Combine(directory, fileName));
								xElement5.SetAttributeValue("Image", fileName);
							}
							if (editer.Value.BackGroundColor.HasValue)
							{
								xElement5.SetAttributeValue("BackgroundColor", editer.Value.BackGroundColor.Value.ToHex(includeHash: true));
							}
						}
					}
				}
				xDocument.Save(manifestPath, SaveOptions.DisableFormatting);
				File.WriteAllBytes(Path.Combine(directory, "CustomCapability.SCCD"), Convert.FromBase64String(SccdBase64));
				string text2 = File.ReadAllText(manifestPath);
				string value = Regex.Match(text2, "<\\s*Extensions\\s*/\\s*>").Value;
				if (value.Length > 0)
				{
					File.WriteAllText(manifestPath, text2.Replace(value, ExtensionsBlock));
				}
				return true;
			});
		}
		catch
		{
			throw;
		}
	}

	private static void UpdateIgnorableNamespaces(XElement package, XNamespace rescap, XNamespace uap4, XNamespace uap10, XNamespace desktop4)
	{
		XAttribute xAttribute = package.Attribute("IgnorableNamespaces");
		string[] array = new string[5] { "uap", "uap4", "uap10", "rescap", "desktop4" };
		if (xAttribute == null)
		{
			package.SetAttributeValue("IgnorableNamespaces", string.Join(" ", array));
		}
		else
		{
			string[] array2 = xAttribute.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			IEnumerable<string> enumerable = array.Except(array2);
			if (enumerable.Any())
			{
				xAttribute.Value = string.Join(" ", array2.Concat(enumerable));
			}
		}
		package.SetAttributeValue(XNamespace.Xmlns + "desktop4", "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4");
		package.SetAttributeValue(XNamespace.Xmlns + "rescap", rescap.NamespaceName);
		package.SetAttributeValue(XNamespace.Xmlns + "uap4", uap4.NamespaceName);
		package.SetAttributeValue(XNamespace.Xmlns + "uap10", uap10.NamespaceName);
	}

	private static void UpdateApplicationTrustLevel(XElement package, XNamespace ns, XNamespace uap10)
	{
		package.Element(ns + "Applications")?.Element(ns + "Application")?.SetAttributeValue(uap10 + "TrustLevel", "mediumIL");
	}

	private static void UpdateCapabilities(XElement package, XNamespace ns, XNamespace rescap, XNamespace uap4)
	{
		XElement xElement = package.Element(ns + "Capabilities");
		if (xElement != null)
		{
			xElement.Elements(rescap + "Capability").Remove();
			xElement.Elements(uap4 + "CustomCapability").Remove();
			List<XElement> list = xElement.Elements(ns + "DeviceCapability").ToList();
			list.ForEach(delegate(XElement c)
			{
				c.Remove();
			});
			xElement.Add(new XElement(rescap + "Capability", new XAttribute("Name", "runFullTrust")), new XElement(uap4 + "CustomCapability", new XAttribute("Name", "Microsoft.coreAppActivation_8wekyb3d8bbwe")));
			if (list.Count > 0)
			{
				list.ForEach(xElement.Add);
			}
			else
			{
				xElement.Add(new XElement(ns + "DeviceCapability", new XAttribute("Name", "internetClient")));
			}
		}
	}
}
