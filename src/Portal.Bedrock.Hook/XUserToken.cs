using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Portal.Bedrock.Hook;

internal static class XUserToken
{
	private sealed class RequestHeader
	{
		public string Name = string.Empty;

		public string Value = string.Empty;
	}

	private sealed class TokenContext
	{
		public bool Utf16;

		public string Method = string.Empty;

		public string RequestTarget = string.Empty;

		public List<string> PolicyHeaderValues = new List<string>();

		public long MaxBodyBytes;

		public byte[] Body = Array.Empty<byte>();

		public byte[] Authorization = Array.Empty<byte>();

		public ushort[] AuthorizationUtf16 = Array.Empty<ushort>();

		public byte[] Signature = Array.Empty<byte>();

		public ushort[] SignatureUtf16 = Array.Empty<ushort>();

		public bool Prepared;
	}

	private sealed record SigningPolicy(long MaxBodyBytes, List<string> ExtraHeaderNames)
	{
		public static SigningPolicy XboxDefault()
		{
			return new SigningPolicy(DefaultXboxMaxBodyBytes, new List<string>());
		}

		public static SigningPolicy XboxFullBody()
		{
			return new SigningPolicy(FullBodyMaxBytes, new List<string>());
		}

		public static SigningPolicy CallerHeaders(List<RequestHeader> headers)
		{
			List<string> list = new List<string>();
			foreach (RequestHeader header in headers)
			{
				if (!IsTransportOrAuthHeader(header.Name) && !list.Any((string n) => n.Equals(header.Name, StringComparison.OrdinalIgnoreCase)))
				{
					list.Add(header.Name);
				}
			}
			return new SigningPolicy(FullBodyMaxBytes, list);
		}
	}

	private const uint TokenOptionsMask = 3u;

	private const int MaxMethodLength = 32;

	private const int MaxUrlLength = 32768;

	private const int MaxHeaderCount = 128;

	private const int MaxHeaderNameLength = 256;

	private const int MaxHeaderValueLength = 32768;

	private const int MaxRequestBodySize = 67108864;

	private const int MaxUtf16InputUnits = 32768;

	private const int DefaultXboxMaxBodyBytes = 8192;

	private const long FullBodyMaxBytes = 2147483647L;

	private static readonly byte[] AddNameBytesStorage = Encoding.ASCII.GetBytes("XUserAddAsync\0");

	private static readonly GCHandle AddNameBytesPin = GCHandle.Alloc(AddNameBytesStorage, GCHandleType.Pinned);

	private static readonly byte[] TokenNameAnsiStorage = Encoding.ASCII.GetBytes("XUserGetTokenAndSignatureAsync\0");

	private static readonly GCHandle TokenNameAnsiPin = GCHandle.Alloc(TokenNameAnsiStorage, GCHandleType.Pinned);

	private static readonly byte[] TokenNameUtf16Storage = Encoding.ASCII.GetBytes("XUserGetTokenAndSignatureUtf16Async\0");

	private static readonly GCHandle TokenNameUtf16Pin = GCHandle.Alloc(TokenNameUtf16Storage, GCHandleType.Pinned);

	public static nint AddNameBytes => AddNameBytesPin.AddrOfPinnedObject();

	public static nint TokenNameAnsi => TokenNameAnsiPin.AddrOfPinnedObject();

	public static nint TokenNameUtf16 => TokenNameUtf16Pin.AddrOfPinnedObject();

	private static TokenContext? CreateContext(nint user, string method, string url, List<RequestHeader> headers, ReadOnlySpan<byte> body, bool utf16)
	{
		if (!XUserObject.IsValidUser(user) || XUserBridge.Session == null)
		{
			return null;
		}
		string relyingParty = RelyingPartyForUrl(url);
		XUserTokenRecord xUserTokenRecord = XUserBridge.Session.TokenForRelyingParty(relyingParty);
		if (xUserTokenRecord == null || xUserTokenRecord.ExpiresAt <= NowEpoch() + 30)
		{
			return null;
		}
		string text = RequestTargetFromUrl(url);
		if (text == null)
		{
			return null;
		}
		SigningPolicy signingPolicy = SigningPolicyForUrl(url, headers);
		List<string> policyHeaderValues = SelectPolicyHeaderValues(headers, signingPolicy.ExtraHeaderNames);
		string text2 = "XBL3.0 x=" + xUserTokenRecord.UserHash + ";" + xUserTokenRecord.Token;
		byte[] bytes = Encoding.ASCII.GetBytes(text2 + "\0");
		List<ushort> list = new List<ushort>(text2.Length + 1);
		string text3 = text2;
		foreach (char item in text3)
		{
			list.Add(item);
		}
		list.Add(0);
		return new TokenContext
		{
			Utf16 = utf16,
			Method = method.ToUpperInvariant(),
			RequestTarget = text,
			PolicyHeaderValues = policyHeaderValues,
			MaxBodyBytes = signingPolicy.MaxBodyBytes,
			Body = body.ToArray(),
			Authorization = bytes,
			AuthorizationUtf16 = list.ToArray(),
			Prepared = false
		};
	}

	private static bool Prepare(TokenContext context)
	{
		if (context.Prepared)
		{
			return true;
		}
		if (XUserBridge.Session == null)
		{
			return false;
		}
		string text = TrimNullTerminator(context.Authorization);
		if (text == null)
		{
			return false;
		}
		Span<byte> span = context.Body.AsSpan(0, (int)Math.Min(context.Body.Length, context.MaxBodyBytes));
		string text2;
		try
		{
			text2 = XUserBridge.Session.SigningKey.SignRequest(context.Method, context.RequestTarget, text, context.PolicyHeaderValues, span);
		}
		catch
		{
			return false;
		}
		Array.Clear(context.Body, 0, context.Body.Length);
		context.Body = Array.Empty<byte>();
		context.PolicyHeaderValues.Clear();
		context.Signature = Encoding.ASCII.GetBytes(text2 + "\0");
		List<ushort> list = new List<ushort>(text2.Length + 1);
		string text3 = text2;
		foreach (char item in text3)
		{
			list.Add(item);
		}
		list.Add(0);
		context.SignatureUtf16 = list.ToArray();
		context.Prepared = true;
		return true;
	}

	private static string? TrimNullTerminator(byte[] value)
	{
		if (value.Length != 0 && value[^1] == 0)
		{
			return Encoding.ASCII.GetString(value, 0, value.Length - 1);
		}
		return Encoding.ASCII.GetString(value);
	}

	private unsafe static nuint? RequiredSize(TokenContext context)
	{
		if (!context.Prepared)
		{
			return null;
		}
		if (context.Utf16)
		{
			return checked((nuint)context.AuthorizationUtf16.Length + (nuint)context.SignatureUtf16.Length) * 2 + (nuint)sizeof(TokenUtf16Data);
		}
		return checked((nuint)context.Authorization.Length + (nuint)context.Signature.Length) + (nuint)sizeof(TokenData);
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserTokenProvider")]
	private unsafe static int TokenProvider(XAsyncOp operation, XAsyncProviderData* providerData)
	{
		if (providerData == null)
		{
			return -2147467261;
		}
		GCHandle gCHandle = (GCHandle)providerData->Context;
		if (!gCHandle.IsAllocated)
		{
			return -2147467261;
		}
		TokenContext tokenContext = (TokenContext)gCHandle.Target!;
		switch (operation)
		{
		case XAsyncOp.Begin:
			return XUserAsync.Schedule((XAsyncBlock*)providerData->AsyncBlock, 0u);
		case XAsyncOp.DoWork:
		{
			nuint? num2 = (Prepare(tokenContext) ? RequiredSize(tokenContext) : ((nuint?)null));
			XUserAsync.Complete((XAsyncBlock*)providerData->AsyncBlock, (!num2.HasValue) ? (-2147467259) : 0, num2.GetValueOrDefault());
			return 0;
		}
		case XAsyncOp.GetResult:
		{
			nuint? num = RequiredSize(tokenContext);
			if (!num.HasValue)
			{
				return -2147467259;
			}
			if (providerData->Buffer == 0 || providerData->BufferSize < (nint)num.Value)
			{
				return -2147024774;
			}
			if (tokenContext.Utf16)
			{
				TokenUtf16Data* buffer = (TokenUtf16Data*)providerData->Buffer;
				ushort* ptr = (ushort*)(buffer + 1);
				ushort* ptr2 = ptr + tokenContext.AuthorizationUtf16.Length;
				tokenContext.AuthorizationUtf16.AsSpan().CopyTo(new Span<ushort>(ptr, tokenContext.AuthorizationUtf16.Length));
				tokenContext.SignatureUtf16.AsSpan().CopyTo(new Span<ushort>(ptr2, tokenContext.SignatureUtf16.Length));
				buffer->TokenCount = tokenContext.AuthorizationUtf16.Length * 2;
				buffer->SignatureCount = tokenContext.SignatureUtf16.Length * 2;
				buffer->Token = (nint)ptr;
				buffer->Signature = (nint)ptr2;
			}
			else
			{
				TokenData* buffer2 = (TokenData*)providerData->Buffer;
				byte* ptr3 = (byte*)buffer2 + sizeof(TokenData);
				byte* ptr4 = ptr3 + tokenContext.Authorization.Length;
				Marshal.Copy(tokenContext.Authorization, 0, (nint)ptr3, tokenContext.Authorization.Length);
				Marshal.Copy(tokenContext.Signature, 0, (nint)ptr4, tokenContext.Signature.Length);
				buffer2->TokenSize = tokenContext.Authorization.Length;
				buffer2->SignatureSize = tokenContext.Signature.Length;
				buffer2->Token = (nint)ptr3;
				buffer2->Signature = (nint)ptr4;
			}
			return 0;
		}
		case XAsyncOp.Cancel:
			return 0;
		case XAsyncOp.Cleanup:
			gCHandle.Free();
			return 0;
		default:
			return -2147467259;
		}
	}

	private unsafe static int BeginTokenRequest(nint user, uint options, string method, string url, List<RequestHeader> headers, nuint bodySize, void* body, XAsyncBlock* asyncBlock, bool utf16)
	{
		if (user == 0 || asyncBlock == null || (bodySize != 0 && body == null))
		{
			return -2147467261;
		}
		if ((options & ~TokenOptionsMask) != 0 || method.Length == 0 || method.Length > MaxMethodLength || !IsAscii(method) || url.Length == 0 || url.Length > MaxUrlLength || !IsAscii(url) || bodySize > MaxRequestBodySize)
		{
			return -2147024809;
		}
		ReadOnlySpan<byte> body2 = ((bodySize == 0) ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>(body, (int)bodySize));
		TokenContext tokenContext = CreateContext(user, method, url, headers, body2, utf16);
		if (tokenContext == null)
		{
			return -2147467259;
		}
		GCHandle value = GCHandle.Alloc(tokenContext);
		int num = XUserAsync.Begin(asyncBlock, (void*)GCHandle.ToIntPtr(value), (void*)(utf16 ? XUserBridge.IdentityUtf16 : XUserBridge.IdentityAnsi), (byte*)(utf16 ? TokenNameUtf16 : TokenNameAnsi), (delegate* unmanaged<XAsyncOp, XAsyncProviderData*, int>)(&TokenProvider));
		if (num < 0)
		{
			value.Free();
		}
		return num;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserGetTokenAndSignatureAsync")]
	public unsafe static int GetTokenAndSignatureAsync(nint self, nint user, uint options, byte* method, byte* url, nuint headerCount, TokenHeader* headers, nuint bodySize, void* body, XAsyncBlock* asyncBlock)
	{
		XUserBridge.Info("XUserGetTokenAndSignatureAsync called");
		if (method == null || url == null || headerCount > MaxHeaderCount || (headerCount != 0 && headers == null))
		{
			return -2147467261;
		}
		if (!CopyAnsiHeaders(headers, headerCount, out List<RequestHeader> copied))
		{
			return -2147467261;
		}
		if (!ReadAnsiStringBounded(method, MaxMethodLength, out string result))
		{
			return -2147024809;
		}
		if (!ReadAnsiStringBounded(url, MaxUrlLength, out string result2))
		{
			return -2147024809;
		}
		return BeginTokenRequest(user, options, result, result2, copied, bodySize, body, asyncBlock, utf16: false);
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserGetTokenAndSignatureResultSize")]
	public unsafe static int GetTokenAndSignatureResultSize(nint self, XAsyncBlock* asyncBlock, nint* size)
	{
		return XUserAsync.GetResultSize(asyncBlock, size);
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserGetTokenAndSignatureResult")]
	public unsafe static int GetTokenAndSignatureResult(nint self, XAsyncBlock* asyncBlock, nuint size, void* buffer, TokenData** data, nint* used)
	{
		if (asyncBlock == null || buffer == null || data == null)
		{
			return -2147467261;
		}
		int result = XUserAsync.GetResult(asyncBlock, (void*)XUserBridge.IdentityAnsi, size, buffer, used);
		if (result >= 0)
		{
			*data = (TokenData*)buffer;
		}
		return result;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserGetTokenAndSignatureUtf16Async")]
	public unsafe static int GetTokenAndSignatureUtf16Async(nint self, nint user, uint options, ushort* method, ushort* url, nuint headerCount, TokenUtf16Header* headers, nuint bodySize, void* body, XAsyncBlock* asyncBlock)
	{
		XUserBridge.Info("XUserGetTokenAndSignatureUtf16Async called");
		if (headerCount > MaxHeaderCount || (headerCount != 0 && headers == null))
		{
			return -2147467261;
		}
		if (!CopyUtf16Headers(headers, headerCount, out List<RequestHeader> copied))
		{
			return -2147467261;
		}
		if (!ReadUtf16StringBounded(method, MaxMethodLength, out string result))
		{
			return -2147024809;
		}
		if (!ReadUtf16StringBounded(url, MaxUrlLength, out string result2))
		{
			return -2147024809;
		}
		return BeginTokenRequest(user, options, result, result2, copied, bodySize, body, asyncBlock, utf16: true);
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserGetTokenAndSignatureUtf16ResultSize")]
	public unsafe static int GetTokenAndSignatureUtf16ResultSize(nint self, XAsyncBlock* asyncBlock, nint* size)
	{
		return XUserAsync.GetResultSize(asyncBlock, size);
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserGetTokenAndSignatureUtf16Result")]
	public unsafe static int GetTokenAndSignatureUtf16Result(nint self, XAsyncBlock* asyncBlock, nuint size, void* buffer, TokenUtf16Data** data, nint* used)
	{
		if (asyncBlock == null || buffer == null || data == null)
		{
			return -2147467261;
		}
		int result = XUserAsync.GetResult(asyncBlock, (void*)XUserBridge.IdentityUtf16, size, buffer, used);
		if (result >= 0)
		{
			*data = (TokenUtf16Data*)buffer;
		}
		return result;
	}

	private unsafe static bool CopyAnsiHeaders(TokenHeader* headers, nuint count, out List<RequestHeader> copied)
	{
		copied = new List<RequestHeader>();
		if (count == 0)
		{
			return true;
		}
		if (headers == null)
		{
			return false;
		}
		for (nuint num = 0u; num < count; num++)
		{
			TokenHeader* ptr = headers + num;
			if (ptr->Name == 0 || ptr->Value == 0)
			{
				return false;
			}
			string text = Marshal.PtrToStringUTF8(ptr->Name);
			string text2 = Marshal.PtrToStringUTF8(ptr->Value);
			if (text == null || text2 == null)
			{
				return false;
			}
			if (!ValidateHeader(text, text2, out RequestHeader header))
			{
				return false;
			}
			copied.Add(header);
		}
		return true;
	}

	private unsafe static bool CopyUtf16Headers(TokenUtf16Header* headers, nuint count, out List<RequestHeader> copied)
	{
		copied = new List<RequestHeader>();
		if (count == 0)
		{
			return true;
		}
		if (headers == null)
		{
			return false;
		}
		for (nuint num = 0u; num < count; num++)
		{
			TokenUtf16Header* ptr = headers + num;
			if (!ReadUtf16StringBounded((ushort*)ptr->Name, MaxHeaderNameLength, out string result))
			{
				return false;
			}
			if (!ReadUtf16StringBounded((ushort*)ptr->Value, MaxHeaderValueLength, out string result2))
			{
				return false;
			}
			if (!ValidateHeader(result, result2, out RequestHeader header))
			{
				return false;
			}
			copied.Add(header);
		}
		return true;
	}

	private static bool ValidateHeader(string name, string value, out RequestHeader header)
	{
		header = new RequestHeader();
		if (name.Length == 0 || name.Length > MaxHeaderNameLength || value.Length > MaxHeaderValueLength || !IsAscii(name) || !IsAscii(value) || !IsHttpTokenBytes(name))
		{
			return false;
		}
		foreach (char c in value)
		{
			if ((c == '\0' || c == '\n' || c == '\r') ? true : false)
			{
				return false;
			}
		}
		header.Name = name.ToLowerInvariant();
		header.Value = value;
		return true;
	}

	private static bool IsHttpTokenBytes(string name)
	{
		foreach (char c in name)
		{
			if (!char.IsAsciiLetterOrDigit(c))
			{
				bool flag;
				switch (c)
				{
				case '!':
				case '#':
				case '$':
				case '%':
				case '&':
				case '\'':
				case '*':
				case '+':
				case '-':
				case '.':
				case '^':
				case '_':
				case '`':
				case '|':
				case '~':
					flag = true;
					break;
				default:
					flag = false;
					break;
				}
				if (!flag)
				{
					return false;
				}
			}
		}
		return true;
	}

	private static bool IsTransportOrAuthHeader(string name)
	{
		switch (name.ToLowerInvariant())
		{
		case "authorization":
		case "signature":
		case "host":
		case "content-length":
			return true;
		default:
			return false;
		}
	}

	private static List<string> SelectPolicyHeaderValues(List<RequestHeader> headers, List<string> policyHeaderNames)
	{
		List<string> list = new List<string>(policyHeaderNames.Count);
		foreach (string policyName in policyHeaderNames)
		{
			list.Add(headers.Find((RequestHeader h) => h.Name.Equals(policyName, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty);
		}
		return list;
	}

	private static SigningPolicy SigningPolicyForUrl(string url, List<RequestHeader> headers)
	{
		string text = UrlHost(url) ?? string.Empty;
		if ((text == "device.mgt.xboxlive.com" || text == "data-vef.xboxlive.com") ? true : false)
		{
			return SigningPolicy.XboxFullBody();
		}
		if (text == "xboxlive.com" || text.EndsWith(".xboxlive.com", StringComparison.Ordinal))
		{
			return SigningPolicy.XboxDefault();
		}
		return SigningPolicy.CallerHeaders(headers);
	}

	private static string RelyingPartyForUrl(string url)
	{
		string text = UrlHost(url) ?? string.Empty;
		bool flag;
		switch (text)
		{
		case "collections.mp.microsoft.com":
		case "purchase.mp.microsoft.com":
		case "displaycatalog.mp.microsoft.com":
		case "inventory.xboxlive.com":
		case "licensing.xboxlive.com":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			return "http://licensing.xboxlive.com";
		}
		if (text == "playfabapi.com" || text.EndsWith(".playfabapi.com", StringComparison.Ordinal))
		{
			return "https://b980a380.minecraft.playfabapi.com/";
		}
		if (text == "multiplayer.minecraft.net" || text.EndsWith(".multiplayer.minecraft.net", StringComparison.Ordinal))
		{
			return "https://multiplayer.minecraft.net/";
		}
		switch (text)
		{
		case "pocket.realms.minecraft.net":
		case "bedrock.frontend.realms.minecraft-services.net":
		case "bedrock.frontendlegacy.realms.minecraft-services.net":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			return "https://pocket.realms.minecraft.net/";
		}
		return "http://xboxlive.com";
	}

	private static string? UrlHost(string url)
	{
		int num = url.IndexOf("://", StringComparison.Ordinal);
		if (num < 0)
		{
			return null;
		}
		string text = url.Substring(num + 3);
		int length = text.Length;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if ((c == '#' || c == '/' || c == '?') ? true : false)
			{
				length = i;
				break;
			}
		}
		text = text.Substring(0, length);
		int num2 = text.LastIndexOf('@');
		string text2 = ((num2 >= 0) ? text.Substring(num2 + 1) : text);
		string text3;
		if (text2.StartsWith('['))
		{
			int num3 = text2.IndexOf(']');
			if (num3 < 0)
			{
				return null;
			}
			text3 = text2.Substring(1, num3 - 1);
		}
		else
		{
			int num4 = text2.IndexOf(':');
			text3 = ((num4 >= 0) ? text2.Substring(0, num4) : text2);
		}
		if (text3.Length != 0)
		{
			return text3.ToLowerInvariant();
		}
		return null;
	}

	private static string? RequestTargetFromUrl(string url)
	{
		int num = url.IndexOf("://", StringComparison.Ordinal);
		if (num < 0)
		{
			return null;
		}
		string text = url.Substring(num + 3);
		if (text.Length == 0)
		{
			return null;
		}
		int num2 = -1;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if ((c == '#' || c == '/' || c == '?') ? true : false)
			{
				num2 = i;
				break;
			}
		}
		if (num2 < 0)
		{
			return "/";
		}
		string text2 = text.Substring(num2);
		if (text2.StartsWith('#'))
		{
			return "/";
		}
		int num3 = text2.IndexOf('#');
		if (num3 >= 0)
		{
			text2 = text2.Substring(0, num3);
		}
		if (!text2.StartsWith('?'))
		{
			return text2;
		}
		return "/" + text2;
	}

	private unsafe static bool ReadAnsiStringBounded(byte* value, int maxBytes, out string result)
	{
		result = string.Empty;
		if (value == null)
		{
			return false;
		}
		int i;
		for (i = 0; i <= maxBytes && value[i] != 0; i++)
		{
		}
		if (i > maxBytes)
		{
			return false;
		}
		byte[] array = new byte[i];
		Marshal.Copy((nint)value, array, 0, i);
		result = Encoding.ASCII.GetString(array);
		return true;
	}

	private unsafe static bool ReadUtf16StringBounded(ushort* value, int maxUnits, out string result)
	{
		result = string.Empty;
		if (value == null)
		{
			return false;
		}
		int i;
		for (i = 0; i <= maxUnits && i < MaxUtf16InputUnits && value[i] != 0; i++)
		{
		}
		if (i > maxUnits)
		{
			return false;
		}
		char[] array = new char[i];
		for (int j = 0; j < i; j++)
		{
			array[j] = (char)value[j];
		}
		result = new string(array);
		return true;
	}

	private static bool IsAscii(string value)
	{
		for (int i = 0; i < value.Length; i++)
		{
			if (value[i] > '\u007f')
			{
				return false;
			}
		}
		return true;
	}

	private static ulong NowEpoch()
	{
		return (ulong)Math.Max(0L, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
	}
}
