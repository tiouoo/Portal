#pragma once

#ifdef PRELOADCPP_EXPORTS
#define PRELOADCPP_API __declspec(dllexport)
#else
#define PRELOADCPP_API __declspec(dllimport)
#endif

extern "C" PRELOADCPP_API const char* GetDllVersion();
extern "C" PRELOADCPP_API const char* GetCommitHash();
void PrintVersionInfo();
