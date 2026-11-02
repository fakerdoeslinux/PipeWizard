using System;
using System.IO;

namespace PipeWizard.Services;

/// <summary>Small append-only log so users can send something useful with a bug report.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static bool _failed;

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        if (_failed)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                File.AppendAllText(
                    AppPaths.LogFile,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            _failed = true;
        }
    }
}
