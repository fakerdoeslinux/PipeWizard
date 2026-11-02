using System;
using System.IO;

namespace PipeWizard.Services;

/// <summary>Where PipeWizard keeps its own state, independent of the port's data directory.</summary>
public static class AppPaths
{
    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string ModsDirectory => Path.Combine(DataDirectory, "mods");

    public static string CacheDirectory => Path.Combine(DataDirectory, "cache");

    public static string DownloadCacheDirectory => Path.Combine(CacheDirectory, "downloads");

    public static string ImageCacheDirectory => Path.Combine(CacheDirectory, "images");

    public static string StagingDirectory => Path.Combine(ModsDirectory, ".staging");

    public static string LogFile => Path.Combine(DataDirectory, "pipewizard.log");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ModsDirectory);
        Directory.CreateDirectory(DownloadCacheDirectory);
        Directory.CreateDirectory(ImageCacheDirectory);
    }

    private static string ResolveDataDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
            {
                return Path.Combine(appData, "PipeWizard");
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "PipeWizard");
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return Path.Combine(xdg, "PipeWizard");
        }

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userHome))
        {
            return Path.Combine(userHome, ".local", "share", "PipeWizard");
        }

        return Path.Combine(Path.GetTempPath(), "PipeWizard");
    }
}
