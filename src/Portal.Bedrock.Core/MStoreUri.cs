using System;
using System.Runtime.InteropServices;

namespace Portal.Bedrock.Core;

[StructLayout(LayoutKind.Sequential, Size = 1)]
public struct MStoreUri
{
	public static Uri CookieUri = new Uri("https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx");

	public static Uri FileListXmlUri = new Uri("https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx");

	public static Uri UpdateUri = new Uri("https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx/secured");

	public static Uri ProductUri = new Uri("https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/9NBLGGH2JHXJ?market=US&locale=en-US&deviceFamily=Windows.Desktop");
}
