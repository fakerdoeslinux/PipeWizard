using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PipeWizard.Models;

namespace PipeWizard.Services;

/// <summary>
/// The shared state behind every page: detected paths, the port's Config.toml,
/// the installed mod library and the launcher.
/// </summary>
public sealed class AppState
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private List<string> _overlayRoots = new();

    public AppSettings Settings { get; private set; } = new();
    public GameBananaClient GameBanana { get; } = new();
    public GameLauncher Launcher { get; } = new();
    public ModInstaller Installer { get; private set; }
    public ObservableCollection<InstalledMod> InstalledMods { get; } = new();

    public string? PortExecutable { get; private set; }

    /// <summary>Port binary built with Newer Super Mario Bros. Wii code linked in.</summary>
    public string? NewerPortExecutable { get; private set; }
    public string? GameFolder { get; private set; }
    public GameValidation Game { get; private set; } = GameValidation.Invalid("The game folder is not set.");
    public string ConfigFile { get; private set; } = string.Empty;
    public TomlConfig Config { get; private set; } = TomlConfig.Load(string.Empty);
    public string? LastError { get; private set; }

    public bool IsPortReady => !string.IsNullOrWhiteSpace(PortExecutable) && File.Exists(PortExecutable);

    /// <summary>Whether a Newer-built port is available for mods that need it.</summary>
    public bool IsNewerPortReady =>
        !string.IsNullOrWhiteSpace(NewerPortExecutable) && File.Exists(NewerPortExecutable);
    public bool IsGameReady => Game.IsValid && GameFolder is not null;
    public bool CanPlay => IsPortReady && IsGameReady && !Launcher.IsRunning;
    public bool IsGameRunning => Launcher.IsRunning;
    public int EnabledModCount => InstalledMods.Count(mod => mod.Enabled);
    public int InstalledModCount => InstalledMods.Count;

    public event EventHandler? Changed;
    public event EventHandler? LaunchStateChanged;

    public AppState()
    {
        AppPaths.EnsureCreated();
        Installer = new ModInstaller(AppPaths.ModsDirectory, AppPaths.DownloadCacheDirectory, GameBanana);
        Launcher.Exited += (_, _) => LaunchStateChanged?.Invoke(this, EventArgs.Empty);
        Settings = AppSettings.Load();
        Refresh();
    }

    /// <summary>Re-detects everything and reloads the config and mod library.</summary>
    public void Refresh()
    {
        LastError = null;

        PortExecutable = PortService.FindExecutable(Settings);
        NewerPortExecutable = PortService.FindNewerExecutable(Settings);
        ConfigFile = PortService.ResolveConfigPath(PortExecutable, Settings);
        Config = TomlConfig.Load(ConfigFile);
        _overlayRoots = ReadOverlayRoots();

        GameFolder = PortService.FindGameFolder(PortExecutable, Config, Settings);
        Game = PortService.ValidateGameFolder(GameFolder);
        if (Game.IsValid)
        {
            GameFolder = PortService.NormalizePath(GameFolder!);
        }

        ReloadInstalledMods();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string SetPortExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return "That file does not exist.";
        }

        Settings.PortExecutable = PortService.NormalizePath(path);
        Settings.Save();
        Refresh();
        return string.Empty;
    }

    public string SetNewerPortExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return "That file does not exist.";
        }

        Settings.NewerPortExecutable = PortService.NormalizePath(path);
        Settings.Save();
        Refresh();
        return string.Empty;
    }

    public string SetConfigFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Settings.ConfigFile = null;
        }
        else
        {
            if (!File.Exists(path))
            {
                return "That Config.toml does not exist yet.";
            }

            Settings.ConfigFile = PortService.NormalizePath(path);
        }

        Settings.Save();
        Refresh();
        return string.Empty;
    }

    public string SetGameFolder(string path)
    {
        var validation = PortService.ValidateGameFolder(path);
        if (!validation.IsValid)
        {
            return validation.Error ?? "That game folder is not usable.";
        }

        var normalized = PortService.NormalizeGameFolder(path)!;
        Settings.GameFolder = normalized;
        Settings.Save();

        GameFolder = normalized;
        Game = validation;
        Config.SetString("paths", "dvd_root", normalized);
        var error = SaveConfig();
        if (error is not null)
        {
            LastError = error;
        }

        Refresh();
        return error ?? string.Empty;
    }

    public string ConfigurePortAndGame(string portExecutable, string gameFolder)
    {
        var portError = SetPortExecutable(portExecutable);
        if (portError.Length > 0)
        {
            return portError;
        }

        var gameError = SetGameFolder(gameFolder);
        if (gameError.Length > 0)
        {
            return gameError;
        }

        return File.Exists(ConfigFile)
            ? string.Empty
            : $"The port was built, but Config.toml could not be created at {ConfigFile}.";
    }

    public string? SaveConfig()
    {
        try
        {
            Config.Save();
            Log.Info($"Saved {ConfigFile}");
            _overlayRoots = ReadOverlayRoots();
            return null;
        }
        catch (Exception exception)
        {
            Log.Error($"Could not save {ConfigFile}.", exception);
            return $"Could not save Config.toml: {exception.Message}";
        }
    }

    public async Task<(bool Success, string Message)> LaunchAsync()
    {
        if (!IsPortReady)
        {
            return (false, "WiiCompiled was not found. Set its location in Settings.");
        }

        if (!IsGameReady)
        {
            return (false, Game.Error ?? "Set your extracted game folder first.");
        }

        // Mods that carry Newer SMBW's code-injection files (like Mario's Final Adventure
        // Wii) need the Newer-built port, which has Newer's loader linked in. Launching
        // them with the vanilla port crashes with "Missing translated function".
        var executable = PortExecutable!;
        if (EnabledModRequiresNewer)
        {
            if (!IsNewerPortReady)
            {
                return (false,
                    "An enabled mod needs the Newer-built port, but WiiCompiled-Newer was not found. " +
                    "Set its location in Settings or build it from the Newer port source.");
            }
            executable = NewerPortExecutable!;
        }

        var dvdRoot = Config.GetString("paths", "dvd_root");
        if (!string.Equals(
                dvdRoot is null ? null : PortService.ResolveRelativeToConfig(dvdRoot, ConfigFile),
                GameFolder,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            Config.SetString("paths", "dvd_root", GameFolder!);
            var error = SaveConfig();
            if (error is not null)
            {
                return (false, error);
            }
        }

        var result = Launcher.Launch(executable, Settings.UseMangoHud);
        LaunchStateChanged?.Invoke(this, EventArgs.Empty);
        return (result.Started, result.Message);
    }

    /// <summary>True when any enabled mod requires the Newer-built port.</summary>
    public bool EnabledModRequiresNewer =>
        InstalledMods.Any(mod => mod.Enabled && mod.RequiresNewer);

    public void SetMangoHud(bool enabled)
    {
        Settings.UseMangoHud = enabled;
        Settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<InstallResult> InstallAsync(
        ModSummary summary,
        GbFile file,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var previous = InstalledMods.FirstOrDefault(mod => mod.ModId == summary.Id);
        var wasEnabled = previous?.Enabled ?? true;
        var previousPriority = previous?.Priority ?? 0;

        var result = await Installer.InstallAsync(summary, file, progress, cancellationToken);
        if (!result.Success || result.Mod is null)
        {
            return result;
        }

        var installed = result.Mod;

        // Remove a stale folder left by an older version whose name changed.
        if (previous is not null &&
            !PathComparer.Equals(previous.InstallDirectory, installed.InstallDirectory) &&
            Directory.Exists(previous.InstallDirectory))
        {
            try
            {
                Directory.Delete(previous.InstallDirectory, true);
            }
            catch (Exception exception)
            {
                Log.Warn($"Could not remove the old copy at {previous.InstallDirectory}: {exception.Message}");
            }
        }

        var order = EnabledRootsInPriorityOrder();
        order.RemoveAll(root => PathComparer.Equals(root, installed.OverlayRoot) ||
                                (previous is not null && PathComparer.Equals(root, previous.OverlayRoot)));
        if (wasEnabled)
        {
            order.Insert(Math.Clamp(previousPriority, 0, order.Count), installed.OverlayRoot);
        }

        var configError = WriteOverlayRoots(order);
        Refresh();
        if (configError is not null)
        {
            return new InstallResult(
                false,
                $"{result.Message} But it could not be enabled: {configError}",
                installed);
        }

        var suffix = wasEnabled ? "It is enabled." : "It is installed but disabled.";
        return new InstallResult(true, $"{result.Message} {suffix}", installed);
    }

    public string SetModEnabled(InstalledMod mod, bool enabled)
    {
        var order = EnabledRootsInPriorityOrder();
        order.RemoveAll(root => PathComparer.Equals(root, mod.OverlayRoot));
        if (enabled)
        {
            order.Insert(0, mod.OverlayRoot);
        }

        var error = WriteOverlayRoots(order);
        Refresh();
        return error ?? string.Empty;
    }

    public string MoveMod(InstalledMod mod, int delta)
    {
        var order = EnabledRootsInPriorityOrder();
        var index = order.FindIndex(root => PathComparer.Equals(root, mod.OverlayRoot));
        var target = index + delta;
        if (index < 0 || target < 0 || target >= order.Count)
        {
            return string.Empty;
        }

        (order[index], order[target]) = (order[target], order[index]);
        var error = WriteOverlayRoots(order);
        if (error is null)
        {
            Refresh();
        }

        return error ?? string.Empty;
    }

    public string RemoveMod(InstalledMod mod)
    {
        var order = EnabledRootsInPriorityOrder();
        order.RemoveAll(root => PathComparer.Equals(root, mod.OverlayRoot));
        var configError = WriteOverlayRoots(order);

        var deleted = Installer.Delete(mod);
        Refresh();

        if (!deleted)
        {
            return "The mod was disabled, but its folder could not be deleted.";
        }

        return configError ?? string.Empty;
    }

    public IReadOnlyList<string> OverlayRoots => _overlayRoots;

    private void ReloadInstalledMods()
    {
        var mods = Installer.LoadInstalled().ToList();
        foreach (var mod in mods)
        {
            var index = FindOverlayIndex(mod.OverlayRoot);
            mod.Enabled = index >= 0;
            mod.Priority = index >= 0 ? index : int.MaxValue;
        }

        InstalledMods.Clear();
        foreach (var mod in mods
                     .OrderBy(mod => mod.Enabled ? 0 : 1)
                     .ThenBy(mod => mod.Priority)
                     .ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase))
        {
            InstalledMods.Add(mod);
        }
    }

    private int FindOverlayIndex(string path)
    {
        for (var i = 0; i < _overlayRoots.Count; i++)
        {
            if (PathComparer.Equals(_overlayRoots[i], path))
            {
                return i;
            }
        }

        return -1;
    }

    private List<string> EnabledRootsInPriorityOrder()
    {
        var result = new List<string>();
        foreach (var mod in InstalledMods.Where(mod => mod.Enabled).OrderBy(mod => mod.Priority))
        {
            if (!result.Contains(mod.OverlayRoot, PathComparer))
            {
                result.Add(mod.OverlayRoot);
            }
        }

        return result;
    }

    private List<string> ReadOverlayRoots()
    {
        var raw = Config.GetStringArray("paths", "overlay_roots");
        if (raw.Count == 0)
        {
            var single = Config.GetString("paths", "overlay_roots");
            if (!string.IsNullOrWhiteSpace(single))
            {
                raw = single
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray();
            }
        }

        var result = new List<string>();
        foreach (var root in raw)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var resolved = PortService.ResolveRelativeToConfig(root, ConfigFile);
            if (!result.Contains(resolved, PathComparer))
            {
                result.Add(resolved);
            }
        }

        return result;
    }

    /// <summary>
    /// Sets the overlay root list to <paramref name="enabledOrder"/> first, followed by
    /// any entries PipeWizard does not own (for example a hand-written path), so a
    /// user's own overlay is preserved.
    /// </summary>
    private string? WriteOverlayRoots(IEnumerable<string> enabledOrder)
    {
        var knownMods = InstalledMods
            .Select(mod => mod.OverlayRoot)
            .ToHashSet(PathComparer);

        var foreign = _overlayRoots
            .Where(root => !knownMods.Contains(root))
            .ToList();

        var combined = new List<string>();
        foreach (var root in enabledOrder.Concat(foreign))
        {
            if (!string.IsNullOrWhiteSpace(root) && !combined.Contains(root, PathComparer))
            {
                combined.Add(root);
            }
        }

        Config.SetStringArray("paths", "overlay_roots", combined);
        var error = SaveConfig();
        if (error is null)
        {
            _overlayRoots = combined;
        }

        return error;
    }
}
