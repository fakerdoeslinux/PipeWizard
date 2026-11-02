using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PipeWizard.Services;

public sealed record GameValidation(bool IsValid, string GameId, int Revision, string? Error)
{
    public static GameValidation Invalid(string error, string gameId = "", int revision = 0) =>
        new(false, gameId, revision, error);
}

/// <summary>
/// Finds the port executable, the extracted disc and the port's Config.toml, and
/// validates the disc the same way the runtime does.
/// </summary>
public static class PortService
{
    private const string ExpectedGameId = "SMNE01";

    public static string ExecutableName => OperatingSystem.IsWindows() ? "WiiCompiled.exe" : "WiiCompiled";

    public static string? FindExecutable(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.PortExecutable))
        {
            var configured = NormalizePath(settings.PortExecutable);
            if (File.Exists(configured))
            {
                return configured;
            }
        }

        foreach (var candidate in CandidateExecutables())
        {
            if (File.Exists(candidate))
            {
                return NormalizePath(candidate);
            }
        }

        var onPath = FindOnPath(ExecutableName);
        return onPath is null ? null : NormalizePath(onPath);
    }

    private static IEnumerable<string> CandidateExecutables()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cwd = Environment.CurrentDirectory;
        var directories = new List<string>
        {
            cwd,
            Path.GetDirectoryName(cwd) ?? cwd,
            Path.Combine(home, "nsmbw-local-build"),
            Path.Combine(home, "Documents", "nsmbw-local-build"),
            Path.Combine(home, "Documents", "nsmbw-rev2", "nsmbw-local-build"),
            Path.Combine(home, "Documents", "NSMB-Wii-Native-Port", "nsmbw-local-build"),
            Path.Combine(home, "NSMB-Wii-Native-Port", "nsmbw-local-build"),
            Path.Combine(home, "Documents", "nsmbw-rev2", "src", "native-build"),
            Path.Combine(home, "Documents", "NSMB-Wii-Native-Port", "native-build"),
        };

        if (OperatingSystem.IsWindows())
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            directories.Add(Path.Combine(home, "Documents", "nsmbw-rev2", "nsmbw-local-build"));
            directories.Add(Path.Combine(documents, "nsmbw-rev2", "nsmbw-local-build"));
            directories.Add(Path.Combine(home, "Documents", "nsmbw-rev2", "native-build"));
        }

        foreach (var directory in directories)
        {
            if (!string.IsNullOrWhiteSpace(directory))
            {
                yield return Path.Combine(directory, ExecutableName);
            }
        }
    }

    public static string ResolveConfigPath(string? executable, AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ConfigFile))
        {
            return NormalizePath(settings.ConfigFile);
        }

        if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
        {
            var directory = Path.GetDirectoryName(NormalizePath(executable));
            if (directory is not null)
            {
                if (FindPortableRoot(directory) is { } portableRoot)
                {
                    return Path.Combine(portableRoot, "UserData", "Config.toml");
                }

                var besideExecutable = Path.Combine(directory, "Config.toml");
                if (File.Exists(besideExecutable))
                {
                    return besideExecutable;
                }

                var portableUserData = Path.Combine(directory, "UserData", "Config.toml");
                if (File.Exists(portableUserData))
                {
                    return portableUserData;
                }
            }
        }

        var osDefault = OperatingSystemDefaultConfigPath();
        if (File.Exists(osDefault))
        {
            return osDefault;
        }

        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var fallback = Path.Combine(local, "WiiCompiled", "Config.toml");
            if (File.Exists(fallback))
            {
                return fallback;
            }
        }

        return osDefault;
    }

    public static string OperatingSystemDefaultConfigPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, "MarioWiiSaveData", "Config.toml");
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "WiiCompiled", "Config.toml");
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return Path.Combine(xdg, "WiiCompiled", "Config.toml");
        }

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userHome, ".local", "share", "WiiCompiled", "Config.toml");
    }

    public static string? FindGameFolder(string? executable, TomlConfig config, AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.GameFolder))
        {
            var configured = ValidateGameFolder(settings.GameFolder);
            if (configured.IsValid)
            {
                return NormalizePath(settings.GameFolder);
            }
        }

        var fromConfig = config.GetString("paths", "dvd_root");
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            var candidate = ResolveRelativeToConfig(fromConfig, config.Path);
            var validation = ValidateGameFolder(candidate);
            if (validation.IsValid)
            {
                return candidate;
            }
        }

        if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
        {
            var directory = Path.GetDirectoryName(NormalizePath(executable))!;
            foreach (var name in new[] { "game", "DATA" })
            {
                var candidate = Path.Combine(directory, name);
                if (ValidateGameFolder(candidate).IsValid)
                {
                    return candidate;
                }
            }

            var parent = Path.GetDirectoryName(directory);
            if (parent is not null)
            {
                var candidate = Path.Combine(parent, "game");
                if (ValidateGameFolder(candidate).IsValid)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    public static GameValidation ValidateGameFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return GameValidation.Invalid("No game folder selected.");
        }

        var path = NormalizePath(folder);
        if (!Directory.Exists(path))
        {
            return GameValidation.Invalid("That folder does not exist.");
        }

        if (!IsDiscRoot(path))
        {
            var nested = Path.Combine(path, "DATA");
            if (IsDiscRoot(nested))
            {
                path = nested;
            }
            else
            {
                return GameValidation.Invalid(
                    "Not an extracted game: the folder needs sys/ and files/ (use Dolphin's Extract Entire Disc).");
            }
        }

        try
        {
            var header = new byte[8];
            using (var stream = File.OpenRead(Path.Combine(path, "sys", "boot.bin")))
            {
                if (stream.Read(header, 0, header.Length) != header.Length)
                {
                    return GameValidation.Invalid("sys/boot.bin is too short to identify the disc.");
                }
            }

            var gameId = Encoding.ASCII.GetString(header, 0, 6);
            var revision = header[7];
            var validationResult = RustNative.ValidateDiscHeader(header, (nuint)header.Length);
            if (validationResult == RustNative.WrongGame ||
                !gameId.Equals(ExpectedGameId, StringComparison.Ordinal))
            {
                return GameValidation.Invalid(
                    $"That is {gameId}, not New Super Mario Bros. Wii (SMNE01).", gameId, revision);
            }

            if (validationResult == RustNative.UnsupportedRevision || revision is not (1 or 2))
            {
                return GameValidation.Invalid(
                    $"That is revision {revision}. The port accepts revision 1 or 2.", gameId, revision);
            }

            if (validationResult != RustNative.Valid)
            {
                return GameValidation.Invalid("The disc header could not be validated.", gameId, revision);
            }

            return new GameValidation(true, gameId, revision, null);
        }
        catch (Exception exception)
        {
            return GameValidation.Invalid($"Could not read sys/boot.bin: {exception.Message}");
        }
    }

    public static bool IsDiscRoot(string path) =>
        Directory.Exists(Path.Combine(path, "files")) &&
        File.Exists(Path.Combine(path, "sys", "boot.bin")) &&
        File.Exists(Path.Combine(path, "sys", "main.dol"));

    public static string NormalizePath(string path)
    {
        var expanded = path.Trim();
        if (expanded.Length >= 2 && expanded[0] == '~' && (expanded[1] == '/' || expanded[1] == '\\'))
        {
            expanded = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                expanded[2..]);
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
        }
        catch
        {
            return expanded;
        }
    }

    public static string ResolveRelativeToConfig(string path, string configPath)
    {
        var normalized = NormalizePath(path);
        if (Path.IsPathRooted(normalized))
        {
            return normalized;
        }

        var directory = Path.GetDirectoryName(configPath) ?? Environment.CurrentDirectory;
        return NormalizePath(Path.Combine(directory, normalized));
    }

    public static string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, executable);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // A malformed PATH entry should not stop the search.
            }
        }

        return null;
    }

    private static string? FindPortableRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        for (var depth = 0; directory is not null && depth <= 4; depth++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "portable.txt")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}

public sealed record LaunchResult(bool Started, string Message, Process? Process = null);

public sealed class GameLauncher
{
    public Process? Process { get; private set; }

    public bool IsRunning
    {
        get
        {
            try
            {
                return Process is { HasExited: false };
            }
            catch
            {
                return false;
            }
        }
    }

    public event EventHandler? Exited;

    public LaunchResult Launch(string executable, bool useMangoHud)
    {
        if (!File.Exists(executable))
        {
            return new LaunchResult(false, "WiiCompiled was not found. Set its location in Settings.");
        }

        if (IsRunning)
        {
            return new LaunchResult(false, "The game is already running.");
        }

        if (useMangoHud && !OperatingSystem.IsWindows() && PortService.FindOnPath("mangohud") is null)
        {
            return new LaunchResult(false, "MangoHud is enabled, but `mangohud` is not installed or not on PATH.");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
            };

            if (useMangoHud && !OperatingSystem.IsWindows())
            {
                startInfo.FileName = "mangohud";
                startInfo.ArgumentList.Add(executable);
            }
            else
            {
                startInfo.FileName = executable;
            }

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.Exited += (_, _) =>
            {
                Process = null;
                Exited?.Invoke(this, EventArgs.Empty);
            };

            if (!process.Start())
            {
                return new LaunchResult(false, "The game process could not be started.");
            }

            Process = process;
            Log.Info($"Launched {executable} (mangohud: {useMangoHud})");
            return new LaunchResult(true, "The game is starting.", process);
        }
        catch (Exception exception)
        {
            Log.Error("Could not launch the game.", exception);
            return new LaunchResult(false, $"Could not start the game: {exception.Message}");
        }
    }
}

public static class PlatformService
{
    public static string? OpenPath(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", new[] { path });
            }
            else
            {
                Process.Start("xdg-open", new[] { path });
            }

            return null;
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }

    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
}
