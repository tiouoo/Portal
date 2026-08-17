using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Portal.Bedrock.Hook;

internal static class XUserObject
{
	private struct XUserAddContext
	{
		public nint Handle;

		public byte Claimed;
	}

	private const int VtableSlotCount = 50;

	private const int GamertagVtableSlotCount = 4;

	private static readonly nint[] UserVtable;

	private static readonly GCHandle UserVtablePin;

	private static readonly nint[] GamertagVtable;

	private static readonly GCHandle GamertagVtablePin;

	private static nint _userObjectPtr;

	private static nint UserObjectAddress
	{
		get
		{
			if (_userObjectPtr == 0)
			{
				AllocateUserObject();
			}
			return _userObjectPtr;
		}
	}

	private static nint UserVtableAddress => UserVtablePin.AddrOfPinnedObject();

	private static nint GamertagVtableAddress => GamertagVtablePin.AddrOfPinnedObject();

	public static nint ProviderInterface => UserObjectAddress;

	public unsafe static nint GamertagInterface => UserObjectAddress + sizeof(nint);

	static XUserObject()
	{
		UserVtable = new nint[50];
		UserVtablePin = GCHandle.Alloc(UserVtable, GCHandleType.Pinned);
		GamertagVtable = new nint[4];
		GamertagVtablePin = GCHandle.Alloc(GamertagVtable, GCHandleType.Pinned);
		FillUserVtable();
		FillGamertagVtable();
	}

	private unsafe static void AllocateUserObject()
	{
		nint num = Marshal.AllocHGlobal(sizeof(XUserObjectLayout));
		*(nint*)num = UserVtableAddress;
		*(nint*)(num + sizeof(nint)) = GamertagVtableAddress;
		_userObjectPtr = num;
	}

	public static bool IsValidUser(nint user)
	{
		if (XUserBridge.Session != null)
		{
			return user == UserObjectAddress;
		}
		return false;
	}

	public static nint GetUserHandle()
	{
		if (XUserBridge.Session == null)
		{
			return 0;
		}
		return UserObjectAddress;
	}

	internal unsafe static int QueryInterface(Guid* iid, void** outPtr)
	{
		if (iid == null || outPtr == null)
		{
			return -2147467261;
		}
		*outPtr = null;
		if (XUserBridge.Session == null)
		{
			return -2147467262;
		}
		Guid guid = *iid;
		XUserBridge.Info("XUserObject::QueryInterface iid=" + guid);
		if (guid == XUserAbi.IidIUnknown || guid == XUserAbi.IidIXUserBase || guid == XUserAbi.IidIXUserAddWithUi || guid == XUserAbi.IidIXUserMsa || guid == XUserAbi.IidIXUserStore || guid == XUserAbi.IidIXUserPlatform || guid == XUserAbi.IidIXUserSignOut)
		{
			*outPtr = (void*)ProviderInterface;
			return 0;
		}
		if (guid == XUserAbi.IidIXUserGamertag)
		{
			*outPtr = (void*)GamertagInterface;
			return 0;
		}
		return -2147467262;
	}

	private unsafe static void FillUserVtable()
	{
		nint[] userVtable = UserVtable;
		userVtable[0] = (nint)(delegate* unmanaged<nint, Guid*, void**, int>)(&SlotQueryInterface);
		userVtable[1] = (nint)(delegate* unmanaged<nint, uint>)(&SlotAddRef);
		userVtable[2] = (nint)(delegate* unmanaged<nint, uint>)(&SlotRelease);
		userVtable[3] = (nint)(delegate* unmanaged<nint, nint, nint*, int>)(&SlotDuplicateHandle);
		userVtable[4] = (nint)(delegate* unmanaged<nint, nint, void>)(&SlotCloseHandle);
		userVtable[5] = (nint)(delegate* unmanaged<nint, nint, nint, int>)(&SlotCompare);
		userVtable[6] = (nint)(delegate* unmanaged<nint, uint*, int>)(&SlotGetMaxUsers);
		userVtable[7] = (nint)(delegate* unmanaged<nint, uint, XAsyncBlock*, int>)(&SlotAddAsync);
		userVtable[8] = (nint)(delegate* unmanaged<nint, XAsyncBlock*, nint*, int>)(&SlotAddResult);
		userVtable[9] = (nint)(delegate* unmanaged<nint, nint, XUserLocalId*, int>)(&SlotGetLocalId);
		userVtable[10] = (nint)(delegate* unmanaged<nint, XUserLocalId, nint*, int>)(&SlotFindUserByLocalId);
		userVtable[11] = (nint)(delegate* unmanaged<nint, nint, ulong*, int>)(&SlotGetId);
		userVtable[12] = (nint)(delegate* unmanaged<nint, ulong, nint*, int>)(&SlotFindUserById);
		userVtable[13] = (nint)(delegate* unmanaged<nint, nint, byte*, int>)(&SlotGetIsGuest);
		userVtable[14] = (nint)(delegate* unmanaged<nint, nint, uint*, int>)(&SlotGetState);
		userVtable[15] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[16] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[17] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[18] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[19] = (nint)(delegate* unmanaged<nint, nint, uint*, int>)(&SlotGetAgeGroup);
		userVtable[20] = (nint)(delegate* unmanaged<nint, nint, uint, int, byte*, uint*, int>)(&SlotCheckPrivilege);
		userVtable[21] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[22] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[23] = (nint)(delegate* unmanaged<nint, nint, uint, byte*, byte*, nuint, TokenHeader*, nuint, void*, XAsyncBlock*, int>)(&XUserToken.GetTokenAndSignatureAsync);
		userVtable[24] = (nint)(delegate* unmanaged<nint, XAsyncBlock*, nint*, int>)(&XUserToken.GetTokenAndSignatureResultSize);
		userVtable[25] = (nint)(delegate* unmanaged<nint, XAsyncBlock*, nuint, void*, TokenData**, nint*, int>)(&XUserToken.GetTokenAndSignatureResult);
		userVtable[26] = (nint)(delegate* unmanaged<nint, nint, uint, ushort*, ushort*, nuint, TokenUtf16Header*, nuint, void*, XAsyncBlock*, int>)(&XUserToken.GetTokenAndSignatureUtf16Async);
		userVtable[27] = (nint)(delegate* unmanaged<nint, XAsyncBlock*, nint*, int>)(&XUserToken.GetTokenAndSignatureUtf16ResultSize);
		userVtable[28] = (nint)(delegate* unmanaged<nint, XAsyncBlock*, nuint, void*, TokenUtf16Data**, nint*, int>)(&XUserToken.GetTokenAndSignatureUtf16Result);
		userVtable[29] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[30] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[31] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[32] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[33] = (nint)(delegate* unmanaged<nint, nint, nint, delegate* unmanaged<void*, XUserLocalId, uint, void>, XTaskQueueRegistrationToken*, int>)(&SlotRegisterForChangeEvent);
		userVtable[34] = (nint)(delegate* unmanaged<nint, XTaskQueueRegistrationToken, byte, byte>)(&SlotUnregisterForChangeEvent);
		userVtable[35] = (nint)(delegate* unmanaged<nint, nint*, int>)(&SlotGetSignOutDeferral);
		userVtable[36] = (nint)(delegate* unmanaged<nint, nint, void>)(&SlotCloseSignOutDeferralHandle);
		userVtable[37] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[38] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[39] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[40] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[41] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[42] = (nint)(delegate* unmanaged<nint, byte>)(&SlotStubBoolean);
		userVtable[43] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[44] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[45] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[46] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[47] = (nint)(delegate* unmanaged<nint, byte>)(&SlotStubBoolean);
		userVtable[48] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
		userVtable[49] = (nint)(delegate* unmanaged<nint, int>)(&SlotStubHresult);
	}

	private unsafe static void FillGamertagVtable()
	{
		nint[] gamertagVtable = GamertagVtable;
		gamertagVtable[0] = (nint)(delegate* unmanaged<nint, Guid*, void**, int>)(&SlotQueryInterface);
		gamertagVtable[1] = (nint)(delegate* unmanaged<nint, uint>)(&SlotAddRef);
		gamertagVtable[2] = (nint)(delegate* unmanaged<nint, uint>)(&SlotRelease);
		gamertagVtable[3] = (nint)(delegate* unmanaged<nint, nint, uint, nuint, byte*, nint*, int>)(&SlotGetGamertag);
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotQueryInterface")]
	private unsafe static int SlotQueryInterface(nint self, Guid* iid, void** outPtr)
	{
		return QueryInterface(iid, outPtr);
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotAddRef")]
	private static uint SlotAddRef(nint self)
	{
		return 2u;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotRelease")]
	private static uint SlotRelease(nint self)
	{
		return 1u;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotDuplicateHandle")]
	private unsafe static int SlotDuplicateHandle(nint self, nint user, nint* duplicated)
	{
		if (duplicated == null)
		{
			return -2147467261;
		}
		nint userHandle = GetUserHandle();
		if (userHandle == 0)
		{
			return -2147467259;
		}
		if (user != userHandle)
		{
			return -2147024809;
		}
		int num = XUserLifecycle.DuplicateActiveHandle();
		if (num < 0)
		{
			return num;
		}
		*duplicated = userHandle;
		return 0;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotCloseHandle")]
	private static void SlotCloseHandle(nint self, nint user)
	{
		if (!IsValidUser(user))
		{
			XUserBridge.Warn("XUserCloseHandle 收到未知用户句柄；已忽略");
		}
		else
		{
			XUserLifecycle.ReleaseUserHandle();
		}
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotCompare")]
	private static int SlotCompare(nint self, nint user1, nint user2)
	{
		return (user1 != user2) ? 1 : 0;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotGetMaxUsers")]
	private unsafe static int SlotGetMaxUsers(nint self, uint* maxUsers)
	{
		if (maxUsers == null)
		{
			return -2147467261;
		}
		*maxUsers = 1u;
		return 0;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserAddProvider")]
	private unsafe static int XUserAddProvider(XAsyncOp operation, XAsyncProviderData* providerData)
	{
		if (providerData == null)
		{
			return -2147467261;
		}
		XUserAddContext* context = (XUserAddContext*)providerData->Context;
		if (context == null)
		{
			return -2147467261;
		}
		switch (operation)
		{
		case XAsyncOp.Begin:
			return XUserAsync.Schedule((XAsyncBlock*)providerData->AsyncBlock, 0u);
		case XAsyncOp.DoWork:
			XUserAsync.Complete((XAsyncBlock*)providerData->AsyncBlock, 0, (nuint)sizeof(nint));
			return 0;
		case XAsyncOp.GetResult:
			if (providerData->Buffer == 0 || providerData->BufferSize < sizeof(nint))
			{
				return -2147024774;
			}
			if (context->Claimed == 0)
			{
				int num = XUserLifecycle.AcquireAddedHandle();
				if (num < 0)
				{
					return num;
				}
				context->Claimed = 1;
			}
			*(nint*)providerData->Buffer = context->Handle;
			return 0;
		case XAsyncOp.Cancel:
			return 0;
		case XAsyncOp.Cleanup:
			Marshal.FreeHGlobal((nint)context);
			return 0;
		default:
			return -2147467259;
		}
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotAddAsync")]
	private unsafe static int SlotAddAsync(nint self, uint options, XAsyncBlock* asyncBlock)
	{
		XUserBridge.Info($"XUserSlotAddAsync options=0x{options:X}");
		if (asyncBlock == null)
		{
			return -2147467261;
		}
		if (XUserLifecycle.State == 1)
		{
			return -1994108671;
		}
		nint userHandle = GetUserHandle();
		if (userHandle == 0)
		{
			return -2147467259;
		}
		XUserAddContext* ptr = (XUserAddContext*)Marshal.AllocHGlobal(sizeof(XUserAddContext));
		ptr->Handle = userHandle;
		ptr->Claimed = 0;
		int num = XUserAsync.Begin(asyncBlock, (void*)ptr, (void*)XUserBridge.IdentityAdd, (byte*)XUserToken.AddNameBytes, (delegate* unmanaged<XAsyncOp, XAsyncProviderData*, int>)(&XUserAddProvider));
		if (num < 0)
		{
			Marshal.FreeHGlobal((nint)ptr);
		}
		return num;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotAddResult")]
	private unsafe static int SlotAddResult(nint self, XAsyncBlock* asyncBlock, nint* user)
	{
		if (asyncBlock == null || user == null)
		{
			return -2147467261;
		}
		return XUserAsync.GetResult(asyncBlock, (void*)XUserBridge.IdentityAdd, (nuint)sizeof(nint), user, null);
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotGetLocalId")]
	private unsafe static int SlotGetLocalId(nint self, nint user, XUserLocalId* localId)
	{
		if (localId == null)
		{
			return -2147467261;
		}
		if (!IsValidUser(user) || XUserBridge.Session == null)
		{
			return -2147024809;
		}
		localId->Value = XUserBridge.Session.LocalId;
		return 0;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotFindUserByLocalId")]
	private unsafe static int SlotFindUserByLocalId(nint self, XUserLocalId localId, nint* user)
	{
		if (user == null)
		{
			return -2147467261;
		}
		if (XUserBridge.Session == null || localId.Value != XUserBridge.Session.LocalId || !XUserLifecycle.ActiveHandleExists)
		{
			return -1994108668;
		}
		int num = XUserLifecycle.DuplicateActiveHandle();
		if (num < 0)
		{
			return num;
		}
		*user = GetUserHandle();
		return 0;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotGetId")]
	private unsafe static int SlotGetId(nint self, nint user, ulong* userId)
	{
		if (userId == null)
		{
			return -2147467261;
		}
		if (!IsValidUser(user) || XUserBridge.Session == null)
		{
			return -2147024809;
		}
		*userId = XUserBridge.Session.Xuid;
		return 0;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotFindUserById")]
	private unsafe static int SlotFindUserById(nint self, ulong userId, nint* user)
	{
		if (user == null)
		{
			return -2147467261;
		}
		if (XUserBridge.Session == null || userId != XUserBridge.Session.Xuid || !XUserLifecycle.ActiveHandleExists)
		{
			return -1994108668;
		}
		int num = XUserLifecycle.DuplicateActiveHandle();
		if (num < 0)
		{
			return num;
		}
		*user = GetUserHandle();
		return 0;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotGetIsGuest")]
	private unsafe static int SlotGetIsGuest(nint self, nint user, byte* isGuest)
	{
		if (isGuest == null)
		{
			return -2147467261;
		}
		if (!IsValidUser(user))
		{
			return -2147024809;
		}
		*isGuest = 0;
		return 0;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotGetState")]
	private unsafe static int SlotGetState(nint self, nint user, uint* state)
	{
		if (state == null)
		{
			return -2147467261;
		}
		if (!IsValidUser(user))
		{
			return -2147024809;
		}
		*state = XUserLifecycle.State;
		return 0;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotGetAgeGroup")]
	private unsafe static int SlotGetAgeGroup(nint self, nint user, uint* ageGroup)
	{
		if (ageGroup == null)
		{
			return -2147467261;
		}
		if (!IsValidUser(user) || XUserBridge.Session == null)
		{
			return -2147024809;
		}
		*ageGroup = XUserBridge.Session.AgeGroup;
		return 0;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotCheckPrivilege")]
	private unsafe static int SlotCheckPrivilege(nint self, nint user, uint options, int privilege, byte* hasPrivilege, uint* denyReason)
	{
		if (hasPrivilege == null || denyReason == null)
		{
			return -2147467261;
		}
		if (!IsValidUser(user) || XUserBridge.Session == null)
		{
			return -2147024809;
		}
		if (XUserLifecycle.State != 0)
		{
			return -1994108671;
		}
		bool flag = privilege >= 0 && Array.IndexOf(XUserBridge.Session.Privileges, (uint)privilege) >= 0;
		*hasPrivilege = (flag ? ((byte)1) : ((byte)0));
		*denyReason = 0u;
		return 0;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotRegisterForChangeEvent")]
	private unsafe static int SlotRegisterForChangeEvent(nint self, nint queue, nint context, delegate* unmanaged<void*, XUserLocalId, uint, void> callback, XTaskQueueRegistrationToken* token)
	{
		return XUserLifecycle.RegisterForChangeEvent(context, callback, token);
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotUnregisterForChangeEvent")]
	private static byte SlotUnregisterForChangeEvent(nint self, XTaskQueueRegistrationToken token, byte wait)
	{
		return XUserLifecycle.UnregisterForChangeEvent(token, wait);
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotGetSignOutDeferral")]
	private unsafe static int SlotGetSignOutDeferral(nint self, nint* deferral)
	{
		return XUserLifecycle.GetSignOutDeferral(deferral);
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotCloseSignOutDeferralHandle")]
	private static void SlotCloseSignOutDeferralHandle(nint self, nint deferral)
	{
		XUserLifecycle.CloseSignOutDeferralHandle(deferral);
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotGetGamertag")]
	private unsafe static int SlotGetGamertag(nint self, nint user, uint component, nuint size, byte* gamertag, nint* used)
	{
		if (gamertag == null)
		{
			return -2147467261;
		}
		if (!IsValidUser(user) || XUserBridge.Session == null)
		{
			return -2147024809;
		}
		string text;
		switch (component)
		{
		case 0u:
		case 1u:
		case 3u:
			text = XUserBridge.Session.Gamertag;
			break;
		case 2u:
			text = string.Empty;
			break;
		default:
			text = null;
			break;
		}
		string text2 = text;
		if (text2 == null)
		{
			return -2147024809;
		}
		nuint num = (nuint)(Encoding.UTF8.GetByteCount(text2) + 1);
		if (used != null)
		{
			*used = (nint)num;
		}
		if (size < num)
		{
			return -2147024774;
		}
		byte[] bytes = Encoding.UTF8.GetBytes(text2);
		Marshal.Copy(bytes, 0, (nint)gamertag, bytes.Length);
		gamertag[bytes.Length] = 0;
		return 0;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotStubHresult")]
	private static int SlotStubHresult(nint self)
	{
		return -2147467263;
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserSlotStubBoolean")]
	private static byte SlotStubBoolean(nint self)
	{
		return 0;
	}
}
