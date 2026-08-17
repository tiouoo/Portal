#include <cstdint>
#include <cstring>
#include <windows.h>

namespace
{
    constexpr DWORD ProcessAccess = PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
                                    PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ;

    [[nodiscard]] bool IsAbsolutePath(const char* path) noexcept
    {
        if (std::strlen(path) < 3)
            return false;

        const bool drivePath = ((path[0] >= 'A' && path[0] <= 'Z') ||
                                (path[0] >= 'a' && path[0] <= 'z')) &&
                               path[1] == ':' && (path[2] == '\\' || path[2] == '/');
        const bool uncPath = (path[0] == '\\' || path[0] == '/') &&
                             (path[1] == '\\' || path[1] == '/');
        return drivePath || uncPath;
    }

    class Handle final
    {
    public:
        explicit Handle(HANDLE handle = nullptr) noexcept : handle_(handle) {}
        ~Handle()
        {
            if (handle_ != nullptr)
                CloseHandle(handle_);
        }

        Handle(const Handle&) = delete;
        Handle& operator=(const Handle&) = delete;

        [[nodiscard]] HANDLE get() const noexcept { return handle_; }
        [[nodiscard]] explicit operator bool() const noexcept { return handle_ != nullptr; }

    private:
        HANDLE handle_;
    };

    class RemoteMemory final
    {
    public:
        RemoteMemory(HANDLE process, void* address) noexcept : process_(process), address_(address) {}
        ~RemoteMemory()
        {
            if (address_ != nullptr)
                VirtualFreeEx(process_, address_, 0, MEM_RELEASE);
        }

        RemoteMemory(const RemoteMemory&) = delete;
        RemoteMemory& operator=(const RemoteMemory&) = delete;

        [[nodiscard]] void* get() const noexcept { return address_; }

    private:
        HANDLE process_;
        void* address_;
    };
}

// Portal native injector ABI. The DLL path must be an absolute ANSI path.
extern "C" __declspec(dllexport) int __cdecl Inject(
    std::int32_t processId,
    const char* dllPath,
    std::uint8_t delayInject,
    std::int32_t delayMs)
{
    if (processId <= 0 || dllPath == nullptr || dllPath[0] == '\0' || delayMs < 0)
        return -1;

    const DWORD attributes = GetFileAttributesA(dllPath);
    if (!IsAbsolutePath(dllPath) || attributes == INVALID_FILE_ATTRIBUTES ||
        (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
        return -2;

    if (delayInject != 0 && delayMs > 0)
        Sleep(static_cast<DWORD>(delayMs));

    const Handle process(OpenProcess(ProcessAccess, FALSE, static_cast<DWORD>(processId)));
    if (!process)
        return -3;

    const HMODULE kernel32 = GetModuleHandleW(L"kernel32.dll");
    const auto loadLibrary = kernel32 == nullptr
        ? nullptr
        : reinterpret_cast<LPTHREAD_START_ROUTINE>(GetProcAddress(kernel32, "LoadLibraryA"));
    if (loadLibrary == nullptr)
        return -4;

    const std::size_t pathSize = std::strlen(dllPath) + 1;
    RemoteMemory remotePath(process.get(), VirtualAllocEx(
        process.get(), nullptr, pathSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
    if (remotePath.get() == nullptr)
        return -5;

    SIZE_T bytesWritten = 0;
    if (!WriteProcessMemory(process.get(), remotePath.get(), dllPath, pathSize, &bytesWritten) ||
        bytesWritten != pathSize)
        return -6;

    const Handle thread(CreateRemoteThread(
        process.get(), nullptr, 0, loadLibrary, remotePath.get(), 0, nullptr));
    if (!thread)
        return -7;

    if (WaitForSingleObject(thread.get(), INFINITE) != WAIT_OBJECT_0)
        return -8;

    DWORD remoteResult = 0;
    if (!GetExitCodeThread(thread.get(), &remoteResult) || remoteResult == 0)
        return -8;

    return 0;
}
