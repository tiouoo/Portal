#pragma once

#include <iostream>
#include <sstream>
#include <iomanip>
#include <chrono>
#include <windows.h>
#include <string>
#include <queue>
#include <mutex>
#include <thread>
#include <condition_variable>
#include <atomic>
#include <fstream>
#include <filesystem>

namespace fs = std::filesystem;

enum class LogLevel {
    INFO,
    WARNING,
    ERR,
    SUCCESS
};

struct LogTask {
    LogLevel level;
    std::string message;
    std::string timestamp;
    std::string context;
};

class Logger {
public:
    static inline HANDLE hConsole = INVALID_HANDLE_VALUE;

private:
    static const WORD INFO_COLOR = FOREGROUND_GREEN | FOREGROUND_INTENSITY;
    static const WORD WARNING_COLOR = FOREGROUND_RED | FOREGROUND_GREEN | FOREGROUND_INTENSITY;
    static const WORD ERROR_COLOR = FOREGROUND_RED | FOREGROUND_INTENSITY;
    static const WORD SUCCESS_COLOR = FOREGROUND_GREEN | FOREGROUND_INTENSITY;
    static const WORD DEFAULT_COLOR = FOREGROUND_RED | FOREGROUND_GREEN | FOREGROUND_BLUE;
    static const WORD CYAN_COLOR = FOREGROUND_BLUE | FOREGROUND_GREEN | FOREGROUND_INTENSITY;

    static inline std::queue<LogTask> logQueue;
    static inline std::mutex queueMutex;
    static inline std::condition_variable cv;
    static inline std::thread workerThread;
    static inline std::atomic<bool> shouldStop{ false };

    static inline std::ofstream logFile;
    static inline std::mutex fileMutex;
    static inline bool fileEnabled = true;
    static inline std::string logFilePath;

    static std::string GetTimestamp() {
        auto now = std::chrono::system_clock::now();
        auto time = std::chrono::system_clock::to_time_t(now);
        auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(now.time_since_epoch()) % 1000;

        std::stringstream ss;
        struct tm tm_info;
        localtime_s(&tm_info, &time);
        ss << std::put_time(&tm_info, "%H:%M:%S") << "." << std::setfill('0') << std::setw(3) << ms.count();
        return ss.str();
    }

    static std::string GetFullTimestamp() {
        auto now = std::chrono::system_clock::now();
        auto time = std::chrono::system_clock::to_time_t(now);
        auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(now.time_since_epoch()) % 1000;

        std::stringstream ss;
        struct tm tm_info;
        localtime_s(&tm_info, &time);
        ss << std::put_time(&tm_info, "%Y-%m-%d %H:%M:%S") << "." << std::setfill('0') << std::setw(3) << ms.count();
        return ss.str();
    }

    static const char* GetLevelString(LogLevel level) {
        switch (level) {
        case LogLevel::INFO:    return "INFO";
        case LogLevel::WARNING: return "WARN";
        case LogLevel::ERR:     return "EROR";
        case LogLevel::SUCCESS: return "SUCC";
        default:                return "LOG ";
        }
    }

    static const char* GetLevelStringFull(LogLevel level) {
        switch (level) {
        case LogLevel::INFO:    return "INFO";
        case LogLevel::WARNING: return "WARNING";
        case LogLevel::ERR:     return "ERROR";
        case LogLevel::SUCCESS: return "SUCCESS";
        default:                return "LOG";
        }
    }

    static WORD GetLevelColor(LogLevel level) {
        switch (level) {
        case LogLevel::INFO:    return CYAN_COLOR;
        case LogLevel::SUCCESS: return INFO_COLOR;
        case LogLevel::WARNING: return WARNING_COLOR;
        case LogLevel::ERR:     return ERROR_COLOR;
        default:                return DEFAULT_COLOR;
        }
    }

    static void WriteToFile(const LogTask& task) {
        if (!fileEnabled) return;

        std::lock_guard<std::mutex> lock(fileMutex);
        if (logFile.is_open()) {
            logFile << GetFullTimestamp() << " "
                << GetLevelStringFull(task.level) << " "
                << "[" << task.context << "] "
                << task.message << std::endl;
            logFile.flush();
        }
    }

    static void ProcessLogs() {
        while (true) {
            std::queue<LogTask> localQueue;
            {
                std::unique_lock<std::mutex> lock(queueMutex);
                cv.wait(lock, [] { return !logQueue.empty() || shouldStop; });

                if (shouldStop && logQueue.empty()) break;

                std::swap(localQueue, logQueue);
            }

            while (!localQueue.empty()) {
                const auto& task = localQueue.front();
                Render(task);
                WriteToFile(task);
                localQueue.pop();
            }
        }
    }

    static void Render(const LogTask& task) {
        if (hConsole == INVALID_HANDLE_VALUE) return;

        std::cout << task.timestamp << " ";

        SetConsoleTextAttribute(hConsole, GetLevelColor(task.level));
        std::cout << GetLevelString(task.level);

        SetConsoleTextAttribute(hConsole, DEFAULT_COLOR);
        std::cout << " [" << task.context << "] " << task.message << "\n";
    }

    static std::string CreateLogDirectory() {
        char exePath[MAX_PATH];
        GetModuleFileNameA(NULL, exePath, MAX_PATH);
        fs::path exeDir = fs::path(exePath).parent_path();

        fs::path logDir = exeDir / "config" / "Portal" / "logs";
        if (!fs::exists(logDir)) {
            fs::create_directories(logDir);
        }

        auto now = std::chrono::system_clock::now();
        auto time = std::chrono::system_clock::to_time_t(now);
        struct tm tm_info;
        localtime_s(&tm_info, &time);

        std::stringstream ss;
        ss << "log_" << std::put_time(&tm_info, "%Y%m%d") << ".log";
        logFilePath = (logDir / ss.str()).string();

        return logFilePath;
    }

public:
    static void Initialize() {
        if (hConsole != INVALID_HANDLE_VALUE) return;

        hConsole = GetStdHandle(STD_OUTPUT_HANDLE);

        std::ios_base::sync_with_stdio(false);
        std::cin.tie(NULL);

        // ��ʼ���ļ���־
        CreateLogDirectory();
        logFile.open(logFilePath, std::ios::out | std::ios::app);
        if (!logFile.is_open()) {
            Logger::Log(LogLevel::ERR, "Unable to open log file!", "Logger");
        }

        if (logFile.is_open()) {
            fileEnabled = true;
            WriteToFile(LogTask{ LogLevel::INFO, "Log file started", GetFullTimestamp(), "Logger" });
        }
        else {
            fileEnabled = false;
        }

        shouldStop = false;
        workerThread = std::thread(ProcessLogs);

        Logger::Log(LogLevel::INFO, "Logger initialized (file: " + std::string(fileEnabled ? "enabled" : "disabled") + ")", "Logger");
        Logger::Log(LogLevel::INFO, "Log file: " + logFilePath, "Logger");
    }

    static void Shutdown() {
        if (fileEnabled) {
            WriteToFile(LogTask{ LogLevel::INFO, "Logger shutting down", GetFullTimestamp(), "Logger" });
            logFile.close();
        }

        shouldStop = true;
        cv.notify_all();
        if (workerThread.joinable()) workerThread.join();
    }

    static void EnableFileLogging(bool enable) {
        fileEnabled = enable;
    }

    static void SetLogPath(const std::string& path) {
        std::lock_guard<std::mutex> lock(fileMutex);
        if (logFile.is_open()) {
            logFile.close();
        }
        logFilePath = path;
        logFile.open(logFilePath, std::ios::out | std::ios::app);
        fileEnabled = logFile.is_open();
    }

    static void Log(LogLevel level, const std::string& message, const std::string& context = "BedrockBoot") {
        LogTask task{ level, message, GetTimestamp(), context };
        {
            std::lock_guard<std::mutex> lock(queueMutex);
            logQueue.push(std::move(task));
        }
        cv.notify_one();
    }

    static void Info(const std::string& msg, const std::string& context = "BedrockBoot") {
        Log(LogLevel::INFO, msg, context);
    }

    static void Warning(const std::string& msg, const std::string& context = "BedrockBoot") {
        Log(LogLevel::WARNING, msg, context);
    }

    static void Error(const std::string& msg, const std::string& context = "BedrockBoot") {
        Log(LogLevel::ERR, msg, context);
    }

    static void Success(const std::string& msg, const std::string& context = "BedrockBoot") {
        Log(LogLevel::SUCCESS, msg, context);
    }
};

