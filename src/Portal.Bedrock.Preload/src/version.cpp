#include "pch.h"
#include "version.h"
#include "version_config.h"
#include "logger.h"

extern "C" PRELOADCPP_API const char* GetDllVersion()
{
    return PRELOADCPP_VERSION;
}

extern "C" PRELOADCPP_API const char* GetCommitHash()
{
    return PRELOADCPP_COMMIT_HASH;
}

void PrintVersionInfo()
{
    Logger::Info("PreLoadCpp Version: " + std::string(PRELOADCPP_VERSION));
    Logger::Info("Commit: " + std::string(PRELOADCPP_COMMIT_HASH));
}
