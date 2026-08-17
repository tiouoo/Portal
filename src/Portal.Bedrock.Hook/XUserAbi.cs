using System;

namespace Portal.Bedrock.Hook;

internal static class XUserAbi
{
	public const int S_OK = 0;

	public const int E_FAIL = -2147467259;

	public const int E_POINTER = -2147467261;

	public const int E_NOINTERFACE = -2147467262;

	public const int E_NOTIMPL = -2147467263;

	public const int E_INVALIDARG = -2147024809;

	public const int E_NOT_SUFFICIENT_BUFFER = -2147024774;

	public const uint XUserStateSignedIn = 0u;

	public const uint XUserStateSigningOut = 1u;

	public const uint XUserStateSignedOut = 2u;

	public const uint XUserAgeGroupUnknown = 0u;

	public const uint XUserAgeGroupChild = 1u;

	public const uint XUserAgeGroupTeen = 2u;

	public const uint XUserAgeGroupAdult = 3u;

	public const int E_GameUserSignedOut = -1994108671;

	public const int E_GameUserDeferralNotAvailable = -1994108669;

	public const int E_GameUserUserNotFound = -1994108668;

	public static readonly Guid IidIUnknown = new Guid(0, 0, 0, 192, 0, 0, 0, 0, 0, 0, 70);

	public static readonly Guid ClsidXUserImpl = new Guid(28103031u, 37369, 18275, 163, 142, 204, 187, 85, 206, 50, 224);

	public static readonly Guid IidIXUserBase = ClsidXUserImpl;

	public static readonly Guid IidIXUserAddWithUi = new Guid(3952867656u, 6364, 19842, 187, 204, 64, 224, 168, 9, 196, 192);

	public static readonly Guid IidIXUserMsa = new Guid(468908229u, 54535, 20050, 187, 5, 247, 38, 208, 231, 17, 97);

	public static readonly Guid IidIXUserStore = new Guid(127145443, 26407, 17279, 142, 157, 143, 143, 155, 36, 57, 247);

	public static readonly Guid IidIXUserPlatform = new Guid(653510260u, 41726, 17658, 182, 196, 163, 35, 188, 148, byte.MaxValue, 83);

	public static readonly Guid IidIXUserSignOut = new Guid(1362220677, 17300, 20198, 140, 24, 191, 181, 212, 174, 241, byte.MaxValue);

	public static readonly Guid IidIXUserGamertag = new Guid(3472161472u, 30326, 19092, 161, 25, 76, 67, 249, 235, 91, 116);

	public static readonly Guid ClsidXThreadingImpl = new Guid(121339339, 8143, 16432, 148, 190, 227, 201, 235, 98, 52, 40);

	public static readonly Guid IidIXThreadingImpl = ClsidXThreadingImpl;

	public const string XboxLiveRp = "http://xboxlive.com";

	public const string PlayFabRp = "https://b980a380.minecraft.playfabapi.com/";

	public const string MultiplayerRp = "https://multiplayer.minecraft.net/";

	public const string RealmsRp = "https://pocket.realms.minecraft.net/";

	public const string LicensingRp = "http://licensing.xboxlive.com";

	public const string XUserBridgeProtocol = "1";

	public const uint LoadLibrarySearchSystem32 = 2048u;
}
