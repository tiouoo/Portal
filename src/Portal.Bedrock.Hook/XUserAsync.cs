using System;

namespace Portal.Bedrock.Hook;

internal static class XUserAsync
{
	private struct XThreadingInterface
	{
		public nint Interface;

		public nint Vtable;
	}

	private const int SlotQueryInterface = 0;

	private const int SlotAddRef = 1;

	private const int SlotRelease = 2;

	private const int SlotAsyncGetResultSize = 4;

	private const int SlotAsyncBegin = 7;

	private const int SlotAsyncSchedule = 9;

	private const int SlotAsyncComplete = 10;

	private const int SlotAsyncGetResult = 11;

	public unsafe static int Begin(XAsyncBlock* asyncBlock, void* context, void* identity, byte* identityName, delegate* unmanaged<XAsyncOp, XAsyncProviderData*, int> provider)
	{
		if (asyncBlock == null || identity == null || identityName == null)
		{
			return -2147467261;
		}
		if (!TryAcquire(out var threading))
		{
			return -2147467259;
		}
		nint slot = Slot(threading.Vtable, SlotAsyncBegin);
		return ((delegate* unmanaged<nint, XAsyncBlock*, void*, void*, byte*, delegate* unmanaged<XAsyncOp, XAsyncProviderData*, int>, int>)slot)(threading.Interface, asyncBlock, context, identity, identityName, provider);
	}

	public unsafe static int Schedule(XAsyncBlock* asyncBlock, uint delayMs)
	{
		if (asyncBlock == null)
		{
			return -2147467261;
		}
		if (!TryAcquire(out var threading))
		{
			return -2147467259;
		}
		nint slot = Slot(threading.Vtable, SlotAsyncSchedule);
		return ((delegate* unmanaged<nint, XAsyncBlock*, uint, int>)slot)(threading.Interface, asyncBlock, delayMs);
	}

	public unsafe static void Complete(XAsyncBlock* asyncBlock, int result, nuint requiredSize)
	{
		if (asyncBlock != null && TryAcquire(out var threading))
		{
			nint slot = Slot(threading.Vtable, SlotAsyncComplete);
			((delegate* unmanaged<nint, XAsyncBlock*, int, nuint, void>)slot)(threading.Interface, asyncBlock, result, requiredSize);
		}
	}

	public unsafe static int GetResultSize(XAsyncBlock* asyncBlock, nint* size)
	{
		if (asyncBlock == null || size == null)
		{
			return -2147467261;
		}
		if (!TryAcquire(out var threading))
		{
			return -2147467259;
		}
		nint slot = Slot(threading.Vtable, SlotAsyncGetResultSize);
		return ((delegate* unmanaged<nint, XAsyncBlock*, nint*, int>)slot)(threading.Interface, asyncBlock, size);
	}

	public unsafe static int GetResult(XAsyncBlock* asyncBlock, void* identity, nuint bufferSize, void* buffer, nint* used)
	{
		if (asyncBlock == null || identity == null || (bufferSize != 0 && buffer == null))
		{
			return -2147467261;
		}
		if (!TryAcquire(out var threading))
		{
			return -2147467259;
		}
		nint slot = Slot(threading.Vtable, SlotAsyncGetResult);
		return ((delegate* unmanaged<nint, XAsyncBlock*, void*, nuint, void*, nint*, int>)slot)(threading.Interface, asyncBlock, identity, bufferSize, buffer, used);
	}

	private unsafe static nint Slot(nint vtable, int index)
	{
		return *(nint*)(vtable + index * sizeof(nint));
	}

	private unsafe static bool TryAcquire(out XThreadingInterface threading)
	{
		threading = default(XThreadingInterface);
		nint num = 0;
		Guid clsidXThreadingImpl = XUserAbi.ClsidXThreadingImpl;
		Guid iidIXThreadingImpl = XUserAbi.IidIXThreadingImpl;
		if (XUserBridge.CallOriginalQuery(&clsidXThreadingImpl, &iidIXThreadingImpl, (void**)(&num)) < 0 || num == 0)
		{
			return false;
		}
		threading.Interface = num;
		threading.Vtable = *(nint*)num;
		return true;
	}
}
