using System;
using System.IO;
using System.Text;

namespace StickyNoteWPF.Services;

public static class Logger
{
    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickyNoteWPF", "logs", "app.log");
    private static readonly RollingLogWriter Writer = new(LogPath);

    public static void Log(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            Writer.Append(line);
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
