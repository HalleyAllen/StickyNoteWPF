using System;
using System.IO;
using System.Text;

namespace StickyNoteWPF.Services;

public static class Logger
{
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "app.log");

    private static readonly object _lock = new();

    public static void Log(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            lock (_lock)
            {
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志失败不阻塞业务
        }
    }

    public static void LogException(string context, Exception ex)
    {
        Log($"[EXCEPTION] {context}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
    }
}
