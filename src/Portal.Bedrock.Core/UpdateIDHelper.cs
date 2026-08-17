using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Portal.Bedrock.Core;

public static class UpdateIDHelper
{
	public static async Task<string> GetUriAsync(string updateId)
	{
		DateTime utcNow = DateTime.UtcNow;
		XmlDocument xmlDocument = new XmlDocument();
		xmlDocument.LoadXml("<s:Envelope xmlns:a=\"http://www.w3.org/2005/08/addressing\" xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\">\n\t<s:Header>\n\t\t<a:Action s:mustUnderstand=\"1\">http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService/GetExtendedUpdateInfo2</a:Action>\n\t\t<a:MessageID>urn:uuid:5754a03d-d8d5-489f-b24d-efc31b3fd32d</a:MessageID>\n\t\t<a:To s:mustUnderstand=\"1\">https://fe3.delivery.mp.microsoft.com/ClientWebService/Client.asmx/secured</a:To>\n\t\t<o:Security s:mustUnderstand=\"1\" xmlns:o=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd\">\n\t\t\t<Timestamp xmlns=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd\">\n\t\t\t\t<Created>\n\t\t\t\t</Created>\n\t\t\t\t<Expires>\n\t\t\t\t</Expires>\n\t\t\t</Timestamp>\n\t\t\t<wuws:WindowsUpdateTicketsToken wsu:id=\"ClientMSA\" xmlns:wsu=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd\" xmlns:wuws=\"http://schemas.microsoft.com/msus/2014/10/WindowsUpdateAuthorization\">\n\t\t\t\t<TicketType Name=\"AAD\" Version=\"1.0\" Policy=\"MBI_SSL\">\n\t\t\t\t</TicketType>\n\t\t\t</wuws:WindowsUpdateTicketsToken>\n\t\t</o:Security>\n\t</s:Header>\n\t<s:Body>\n\t\t<GetExtendedUpdateInfo2 xmlns=\"http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService\">\n\t\t\t<updateIDs>\n\t\t\t\t<UpdateIdentity>\n\t\t\t\t\t<UpdateID>\n\t\t\t\t\t</UpdateID>\n\t\t\t\t\t<RevisionNumber>1</RevisionNumber>\n\t\t\t\t</UpdateIdentity>\n\t\t\t</updateIDs>\n\t\t\t<infoTypes>\n\t\t\t\t<XmlUpdateFragmentType>FileUrl</XmlUpdateFragmentType>\n\t\t\t</infoTypes>\n\t\t\t<deviceAttributes>\n\t\t\t</deviceAttributes>\n\t\t</GetExtendedUpdateInfo2>\n\t</s:Body>\n</s:Envelope>");
		xmlDocument.GetElementsByTagName("UpdateID")[0].InnerText = updateId;
		xmlDocument.GetElementsByTagName("Created")[0].InnerText = utcNow.ToString("o");
		xmlDocument.GetElementsByTagName("Expires")[0].InnerText = utcNow.AddMinutes(5.0).ToString("o");
		xmlDocument.GetElementsByTagName("deviceAttributes")[0].InnerText = SoapApi.DeviceAttributes;
		string innerXml = xmlDocument.InnerXml;
		using HttpClient client = new HttpClient();
		client.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/soap+xml; charset=utf-8");
		using StringContent content = new StringContent(innerXml, Encoding.UTF8, "application/soap+xml");
		using HttpResponseMessage response = await client.PostAsync(MStoreUri.UpdateUri.OriginalString, content);
		response.EnsureSuccessStatusCode();
		XDocument xDocument = XDocument.Parse(await response.Content.ReadAsStringAsync());
		XNamespace ns = "http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService";
		List<string> urls = (from x in xDocument.Descendants(ns + "Url")
			select WebUtility.HtmlDecode(x.Value)).ToList();
		return urls.FirstOrDefault((string url) => url.Contains("?P1=") || url.Contains("tlu.dl.") || url.Contains("&P2=") || url.Contains("%3d") || url.Length > 150) ?? urls.LastOrDefault();
	}
}
