using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Portal.Bedrock.Preload;

internal enum LogLevel
{
    Info,
    Warning,
    Error,
    Success,
}

/// <summary>
/// 鎺у埗鍙?+ 鏂囦欢鍙岄€氶亾鏃ュ織銆傜洿鎺ュ熀浜庡師鐢熷彞鏌勫啓鍏ワ紙WriteFile/WriteConsoleW锛夛紝
/// 鍙畨鍏ㄥ湴鍦ㄦ寕閽╁洖璋冧笌宸ヤ綔绾跨▼涓娇鐢紱鏈垵濮嬪寲鏃跺厛鍏ラ槦锛屽垵濮嬪寲鍚庣粺涓€钀界洏銆?/// </summary>
internal static unsafe partial class Logger
{
    private const uint FileAppendData = 0x0004;
    private const uint ShareReadWriteDelete = 0x0007;
    private const uint OpenAlways = 4;
    private const uint StdOutputHandle = 0xFFFFFFF5;

    private const ushort ColorInfo = 0x0B;      // 钃?缁?浜害
    private const ushort ColorSuccess = 0x0A;   // 缁?浜害
    private const ushort ColorWarning = 0x0E;   // 绾?缁?浜害
    private const ushort ColorError = 0x0C;     // 绾?浜害
    private const ushort ColorDefault = 0x07;   // 绾?缁?钃?
    private static readonly Lock QueueLock = new();
    private static readonly Queue<(LogLevel Level, string Message, string Context)> Pending = new();
    private static readonly Lock WriteLock = new();

    private static nint _logHandle = InvalidHandle;
    private static nint _consoleOut = InvalidHandle;
    private static bool _ready;
    private static bool _console;
    private static bool _fileEnabled = true;
    private static string _logPath = "";
    private static readonly nint InvalidHandle = new(-1);

    private static string TimeStamp
    {
        get
        {
            NativeMethods.GetLocalTime(out SystemTime time);
            Span<char> buffer = stackalloc char[12];
            int pos = 0;
            Append2(buffer, ref pos, time.Hour);
            buffer[pos++] = ':';
            Append2(buffer, ref pos, time.Minute);
            buffer[pos++] = ':';
            Append2(buffer, ref pos, time.Second);
            buffer[pos++] = '.';
            Append3(buffer, ref pos, time.Milliseconds);
            return new string(buffer[..pos]);
        }
    }

    private static string FullTimeStamp
    {
        get
        {
            NativeMethods.GetLocalTime(out SystemTime time);
            Span<char> buffer = stackalloc char[23];
            int pos = 0;
            Append4(buffer, ref pos, time.Year);
            buffer[pos++] = '-';
            Append2(buffer, ref pos, time.Month);
            buffer[pos++] = '-';
            Append2(buffer, ref pos, time.Day);
            buffer[pos++] = ' ';
            Append2(buffer, ref pos, time.Hour);
            buffer[pos++] = ':';
            Append2(buffer, ref pos, time.Minute);
            buffer[pos++] = ':';
            Append2(buffer, ref pos, time.Second);
            buffer[pos++] = '.';
            Append3(buffer, ref pos, time.Milliseconds);
            return new string(buffer[..pos]);
        }
    }

    // 纯字符补零，完全不经过文化相关的数字格式化（ICU 在 reverse P/Invoke 上下文中不安全）。
    private static void Append2(Span<char> buffer, ref int pos, int value)
    {
        buffer[pos++] = (char)('0' + value / 10);
        buffer[pos++] = (char)('0' + value % 10);
    }

    private static void Append3(Span<char> buffer, ref int pos, int value)
    {
        buffer[pos++] = (char)('0' + value / 100);
        buffer[pos++] = (char)('0' + value / 10 % 10);
        buffer[pos++] = (char)('0' + value % 10);
    }

    private static void Append4(Span<char> buffer, ref int pos, int value)
    {
        buffer[pos++] = (char)('0' + value / 1000);
        buffer[pos++] = (char)('0' + value / 100 % 10);
        buffer[pos++] = (char)('0' + value / 10 % 10);
        buffer[pos++] = (char)('0' + value % 10);
    }

    private static string ConsoleLabel(LogLevel level) => level switch
    {
        LogLevel.Info => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "EROR",
        LogLevel.Success => "SUCC",
        _ => "LOG ",
    };

    private static string FileLabel(LogLevel level) => level switch
    {
        LogLevel.Info => "INFO",
        LogLevel.Warning => "WARNING",
        LogLevel.Error => "ERROR",
        LogLevel.Success => "SUCCESS",
        _ => "LOG",
    };

    private static ushort ColorOf(LogLevel level) => level switch
    {
        LogLevel.Info => ColorInfo,
        LogLevel.Success => ColorSuccess,
        LogLevel.Warning => ColorWarning,
        LogLevel.Error => ColorError,
        _ => ColorDefault,
    };

    public static void Initialize(bool consoleEnabled, string fileName)
    {
        if (_ready)
            return;

        _console = consoleEnabled;
        _consoleOut = consoleEnabled
            ? NativeMethods.GetStdHandle(StdOutputHandle)
            : InvalidHandle;

        _logPath = ResolveLogPath(fileName);
        _logHandle = NativeMethods.CreateFileW(_logPath, FileAppendData, ShareReadWriteDelete,
            nint.Zero, OpenAlways, 0x80 /* FILE_ATTRIBUTE_NORMAL */, nint.Zero);
        _fileEnabled = _logHandle != InvalidHandle;

        _ready = true;
        FlushPending();

        Info("Log file started", "Logger");
        Info(_fileEnabled ? "Logger initialized (file: enabled)" : "Logger initialized (file: disabled)", "Logger");
        Info($"Log file: {_logPath}", "Logger");
    }

    public static void Info(string message, string context = "Portal") => Write(LogLevel.Info, message, context);
    public static void Warning(string message, string context = "Portal") => Write(LogLevel.Warning, message, context);
    public static void Error(string message, string context = "Portal") => Write(LogLevel.Error, message, context);
    public static void Success(string message, string context = "Portal") => Write(LogLevel.Success, message, context);

    /// <summary>
    /// 从 <c>UnmanagedCallersOnly</c> 挂钩回调中安全地记录：初始化前直接丢弃（避免在
    /// reverse P/Invoke 上下文中触碰待处理队列），初始化后走原生句柄写入。
    /// </summary>
    internal static void WriteFromHook(LogLevel level, string message, string context = "Portal")
    {
        if (!_ready)
            return;
        Emit(level, message, context);
    }

    private static void Write(LogLevel level, string message, string context)
    {
        if (!_ready)
        {
            lock (QueueLock)
            {
                if (!_ready)
                {
                    Pending.Enqueue((level, message, context));
                    return;
                }
            }
        }

        Emit(level, message, context);
    }

    private static void FlushPending()
    {
        lock (QueueLock)
        {
            while (Pending.TryDequeue(out var task))
                Emit(task.Level, task.Message, task.Context);
        }
    }

    private static void Emit(LogLevel level, string message, string context)
    {
        if (_console)
            Render(level, message, context);
        WriteFile(level, message, context);
    }

    private static string ResolveLogPath(string fileName)
    {
        string safeName = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(safeName) ||
            !Path.GetExtension(safeName).Equals(".log", StringComparison.OrdinalIgnoreCase))
        {
            safeName = "native.log";
        }

        string directory = Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath) ?? ".",
            "config", "Portal", "logs");
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, safeName);
    }

    private static void Render(LogLevel level, string message, string context)
    {
        if (_consoleOut == InvalidHandle)
            return;

        lock (WriteLock)
        {
            NativeMethods.SetConsoleTextAttribute(_consoleOut, ColorOf(level));
            WriteConsole($"{TimeStamp} {ConsoleLabel(level)}");
            NativeMethods.SetConsoleTextAttribute(_consoleOut, ColorDefault);
            WriteConsole($" [{context}] {message}\n");
        }
    }

    private static void WriteConsole(string text)
    {
        fixed (char* buffer = text)
        {
            NativeMethods.WriteConsoleW(_consoleOut, buffer, (uint)text.Length, out _, nint.Zero);
        }
    }

    private static void WriteFile(LogLevel level, string message, string context)
    {
        if (!_fileEnabled)
            return;

        string line = $"{FullTimeStamp} {FileLabel(level)} [{context}] {message}\n";
        byte[] bytes = Encoding.UTF8.GetBytes(line);

        lock (WriteLock)
        {
            fixed (byte* buffer = bytes)
            {
                NativeMethods.WriteFile(_logHandle, buffer, (uint)bytes.Length, out _, nint.Zero);
            }
        }
    }
}

