#include <windows.h>
#include <cstdint>

namespace
{
    constexpr std::uint16_t DosSignature = 0x5A4D;
    constexpr std::uint32_t NtSignature = 0x00004550;
    constexpr std::uint32_t BreakpointException = 0x80000003;

    void* g_handler = nullptr;
    std::uint8_t* g_entryPoint = nullptr;
    std::uint8_t g_originalByte = 0;
    volatile LONG g_triggered = 0;

    DWORD WINAPI PreloadThread(void*) noexcept;

    void Log(const wchar_t* state) noexcept
    {
        wchar_t modulePath[MAX_PATH]{};
        const DWORD length = GetModuleFileNameW(nullptr, modulePath, MAX_PATH);
        if (length == 0 || length >= MAX_PATH)
            return;
        for (DWORD index = length; index > 0; --index)
        {
            if (modulePath[index - 1] == L'\\')
            {
                modulePath[index] = L'\0';
                break;
            }
        }

        const wchar_t suffix[] = L"config\\Portal\\logs\\bootstrap.log";
        if (wcslen(modulePath) + wcslen(suffix) >= MAX_PATH)
            return;
        wcscat_s(modulePath, suffix);
        const HANDLE file = CreateFileW(modulePath, FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_ALWAYS,
            FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE)
            return;
        DWORD written = 0;
        WriteFile(file, state, static_cast<DWORD>(wcslen(state) * sizeof(wchar_t)), &written, nullptr);
        static constexpr wchar_t newline[] = L"\r\n";
        WriteFile(file, newline, sizeof(newline) - sizeof(wchar_t), &written, nullptr);
        CloseHandle(file);
    }

    bool WriteByte(std::uint8_t* address, std::uint8_t value) noexcept
    {
        DWORD oldProtect = 0;
        if (!VirtualProtect(address, 1, PAGE_EXECUTE_READWRITE, &oldProtect))
            return false;

        *address = value;
        FlushInstructionCache(GetCurrentProcess(), address, 1);
        DWORD ignored = 0;
        return VirtualProtect(address, 1, oldProtect, &ignored) != FALSE;
    }

    std::uint8_t* FindEntryPoint() noexcept
    {
        auto* image = reinterpret_cast<std::uint8_t*>(GetModuleHandleW(nullptr));
        if (image == nullptr || *reinterpret_cast<std::uint16_t*>(image) != DosSignature)
            return nullptr;

        const auto peOffset = *reinterpret_cast<std::int32_t*>(image + 0x3C);
        if (peOffset < 0x40 || peOffset > 16 * 1024 * 1024)
            return nullptr;

        auto* nt = image + peOffset;
        if (*reinterpret_cast<std::uint32_t*>(nt) != NtSignature)
            return nullptr;

        const auto entryRva = *reinterpret_cast<std::uint32_t*>(nt + 4 + 20 + 16);
        const auto imageSize = *reinterpret_cast<std::uint32_t*>(nt + 4 + 20 + 56);
        return entryRva == 0 || entryRva >= imageSize ? nullptr : image + entryRva;
    }

    LONG CALLBACK OnBreakpoint(EXCEPTION_POINTERS* exception) noexcept
    {
        if (exception == nullptr || exception->ExceptionRecord == nullptr ||
            exception->ContextRecord == nullptr ||
            exception->ExceptionRecord->ExceptionCode != BreakpointException ||
            exception->ExceptionRecord->ExceptionAddress != g_entryPoint)
            return EXCEPTION_CONTINUE_SEARCH;

        if (InterlockedExchange(&g_triggered, 1) != 0)
            return EXCEPTION_CONTINUE_SEARCH;

        if (!WriteByte(g_entryPoint, g_originalByte))
            return EXCEPTION_CONTINUE_SEARCH;

        Log(L"hit");

        HANDLE thread = CreateThread(nullptr, 0, PreloadThread, nullptr, 0, nullptr);
        if (thread == nullptr)
        {
            Log(L"preload-thread-create-failed");
        }
        else
        {
            constexpr DWORD preloadTimeoutMs = 30000;
            const DWORD waitResult = WaitForSingleObject(thread, preloadTimeoutMs);
            if (waitResult == WAIT_OBJECT_0)
                Log(L"preload-called");
            else if (waitResult == WAIT_TIMEOUT)
                Log(L"preload-timeout");
            else
                Log(L"preload-wait-failed");
            CloseHandle(thread);
        }

        if (g_handler != nullptr)
        {
            RemoveVectoredExceptionHandler(g_handler);
            g_handler = nullptr;
        }

#if defined(_M_X64)
        exception->ContextRecord->Rip = reinterpret_cast<DWORD64>(g_entryPoint);
#endif
        return EXCEPTION_CONTINUE_EXECUTION;
    }

    DWORD WINAPI PreloadThread(void*) noexcept
    {
        Log(L"preload-thread-start");

        HMODULE preload = GetModuleHandleW(L"Portal.Preload.dll");
        if (preload == nullptr)
        {
            Log(L"preload-load-start");
            wchar_t preloadPath[MAX_PATH]{};
            const DWORD pathLength = GetModuleFileNameW(nullptr, preloadPath, MAX_PATH);
            if (pathLength > 0 && pathLength < MAX_PATH)
            {
                for (DWORD index = pathLength; index > 0; --index)
                {
                    if (preloadPath[index - 1] == L'\\')
                    {
                        preloadPath[index] = L'\0';
                        break;
                    }
                }
                wcscat_s(preloadPath, L"Portal.Preload.dll");
                preload = LoadLibraryW(preloadPath);
            }
            Log(preload == nullptr ? L"preload-load-failed" : L"preload-load-returned");
            if (preload == nullptr)
                return 0;
        }

        const auto load = reinterpret_cast<void (*)()>(GetProcAddress(preload, "Load"));
        if (load == nullptr)
        {
            Log(L"preload-export-missing");
            return 0;
        }

        Log(L"preload-call-start");
        load();
        Log(L"preload-returned");
        return 0;
    }

    bool Arm() noexcept
    {
        g_entryPoint = FindEntryPoint();
        if (g_entryPoint == nullptr || *g_entryPoint == 0xCC)
            return false;

        g_originalByte = *g_entryPoint;
        g_handler = AddVectoredExceptionHandler(1, OnBreakpoint);
        if (g_handler == nullptr)
            return false;

        if (WriteByte(g_entryPoint, 0xCC))
        {
            Log(L"armed");
            return true;
        }

        RemoveVectoredExceptionHandler(g_handler);
        g_handler = nullptr;
        return false;
    }
}

extern "C" __declspec(dllexport) void Load()
{
}

BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID reserved)
{
    if (reason == DLL_PROCESS_ATTACH && reserved != nullptr)
        Arm();
    return TRUE;
}
