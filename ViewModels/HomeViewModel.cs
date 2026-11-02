using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Platform.Storage;
using PipeWizard.Models;
using PipeWizard.Services;

namespace PipeWizard.ViewModels;

public sealed class HomeViewModel : ObservableObject
{
    private readonly MainWindowViewModel _shell;
    private string _launchMessage = string.Empty;
    private bool _launchMessageIsError;
    private string _setupStatus = string.Empty;
    private bool _isSettingUp;
    private CancellationTokenSource? _setupCancellation;

    public HomeViewModel(MainWindowViewModel shell)
    {
        _shell = shell;
        PlayCommand = new AsyncRelayCommand(PlayAsync);
        BrowseModsCommand = new RelayCommand(() => _shell.Navigate("mods"));
        ManageModsCommand = new RelayCommand(() => _shell.Navigate("installed"));
        OpenConfigCommand = new RelayCommand(OpenConfig);
        OpenGameFolderCommand = new RelayCommand(OpenGameFolder);
        OpenModsFolderCommand = new RelayCommand(() => Open(AppPaths.ModsDirectory));
        OpenSettingsCommand = new RelayCommand(() => _shell.Navigate("settings"));
        AutoDetectCommand = new RelayCommand(UseAutoDetect);
        SetupNativePortCommand = new AsyncRelayCommand(SetupNativePortAsync);
        CancelSetupCommand = new RelayCommand(CancelSetup);
        Refresh();
    }

    private AppState State => _shell.State;

    public ICommand PlayCommand { get; }
    public ICommand BrowseModsCommand { get; }
    public ICommand ManageModsCommand { get; }
    public ICommand OpenConfigCommand { get; }
    public ICommand OpenGameFolderCommand { get; }
    public ICommand OpenModsFolderCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand AutoDetectCommand { get; }
    public ICommand SetupNativePortCommand { get; }
    public ICommand CancelSetupCommand { get; }

    public bool IsPortOk => State.IsPortReady;
    public bool ShouldOfferSourceSetup => !State.IsPortReady;
    public bool IsSetupSupported =>
        OperatingSystem.IsLinux() &&
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
        System.Runtime.InteropServices.Architecture.X64;
    public bool IsSettingUp
    {
        get => _isSettingUp;
        private set
        {
            if (SetProperty(ref _isSettingUp, value))
            {
                OnPropertyChanged(nameof(IsNotSettingUp));
            }
        }
    }

    public bool IsNotSettingUp => !IsSettingUp;
    public string SetupStatus
    {
        get => _setupStatus;
        private set => SetProperty(ref _setupStatus, value);
    }

    public bool IsGameOk => State.IsGameReady;
    public bool IsConfigOk => File.Exists(State.ConfigFile);

    public string PortPath => State.PortExecutable ?? "WiiCompiled was not found";
    public string GamePath => State.GameFolder ?? "No extracted disc selected";
    public string ConfigPath => State.ConfigFile;

    public string PortStatus => IsPortOk ? "Found" : "Locate";
    public string GameStatus => IsGameOk ? $"Revision {State.Game.Revision} verified" : "Choose folder";
    public string ConfigStatus => IsConfigOk ? "Found" : "Will be created";

    public string GameError => State.Game.Error ?? string.Empty;
    public bool HasGameError => !string.IsNullOrWhiteSpace(State.Game.Error) && !IsGameOk;

    public string HeroTitle => State.IsGameReady
        ? (State.EnabledModCount > 0 ? "Ready to play, modded" : "Ready to play")
        : "Finish setting up";

    public string HeroSubtitle => State.IsGameReady
        ? $"{State.Game.GameId} · Revision {State.Game.Revision} · {State.EnabledModCount} mod{(State.EnabledModCount == 1 ? string.Empty : "s")} enabled"
        : State.IsPortReady
            ? "PipeWizard found the port. Now point it at your extracted disc."
            : "Locate the port you built, then choose the folder you extracted with Dolphin.";

    public bool CanPlay => State.CanPlay;
    public string PlayLabel => State.IsGameRunning ? "Running" : "Play";
    public bool IsGameRunning => State.IsGameRunning;

    public string ModsSummary => State.EnabledModCount == 0
        ? "No mods enabled"
        : $"{State.EnabledModCount} mod{(State.EnabledModCount == 1 ? string.Empty : "s")} enabled";

    public IReadOnlyList<InstalledMod> EnabledMods => State.InstalledMods
        .Where(mod => mod.Enabled)
        .OrderBy(mod => mod.Priority)
        .Take(4)
        .ToList();

    public bool HasEnabledMods => State.EnabledModCount > 0;
    public bool HasMoreMods => State.EnabledModCount > 4;
    public string MoreModsText => State.EnabledModCount > 4 ? $"+{State.EnabledModCount - 4} more" : string.Empty;

    public bool MangoHud
    {
        get => State.Settings.UseMangoHud;
        set
        {
            if (State.Settings.UseMangoHud == value)
            {
                return;
            }

            State.SetMangoHud(value);
            OnPropertyChanged();
        }
    }

    public bool ShowFps
    {
        get => State.Config.GetBool("video", "show_fps") ?? false;
        set
        {
            if ((State.Config.GetBool("video", "show_fps") ?? false) == value)
            {
                return;
            }

            State.Config.SetBool("video", "show_fps", value);
            var error = State.SaveConfig();
            OnPropertyChanged();
            ShowMessage(error ?? (value ? "FPS counter enabled." : "FPS counter disabled."), error is not null);
        }
    }

    public string LaunchMessage
    {
        get => _launchMessage;
        private set
        {
            if (SetProperty(ref _launchMessage, value))
            {
                OnPropertyChanged(nameof(HasLaunchMessage));
            }
        }
    }

    public bool LaunchMessageIsError
    {
        get => _launchMessageIsError;
        private set => SetProperty(ref _launchMessageIsError, value);
    }

    public bool HasLaunchMessage => !string.IsNullOrWhiteSpace(LaunchMessage);

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsPortOk));
        OnPropertyChanged(nameof(ShouldOfferSourceSetup));
        OnPropertyChanged(nameof(IsGameOk));
        OnPropertyChanged(nameof(IsConfigOk));
        OnPropertyChanged(nameof(PortPath));
        OnPropertyChanged(nameof(GamePath));
        OnPropertyChanged(nameof(ConfigPath));
        OnPropertyChanged(nameof(PortStatus));
        OnPropertyChanged(nameof(GameStatus));
        OnPropertyChanged(nameof(ConfigStatus));
        OnPropertyChanged(nameof(GameError));
        OnPropertyChanged(nameof(HasGameError));
        OnPropertyChanged(nameof(HeroTitle));
        OnPropertyChanged(nameof(HeroSubtitle));
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(PlayLabel));
        OnPropertyChanged(nameof(IsGameRunning));
        OnPropertyChanged(nameof(ModsSummary));
        OnPropertyChanged(nameof(EnabledMods));
        OnPropertyChanged(nameof(HasEnabledMods));
        OnPropertyChanged(nameof(HasMoreMods));
        OnPropertyChanged(nameof(MoreModsText));
        OnPropertyChanged(nameof(MangoHud));
        OnPropertyChanged(nameof(ShowFps));
    }

    public void ShowMessage(string message, bool isError)
    {
        LaunchMessage = message;
        LaunchMessageIsError = isError;
    }

    public void SetPortExecutable(string path)
    {
        var error = State.SetPortExecutable(path);
        ShowMessage(error.Length == 0 ? "Port location saved." : error, error.Length > 0);
    }

    public void SetGameFolder(string path)
    {
        var error = State.SetGameFolder(path);
        ShowMessage(error.Length == 0 ? "Game folder saved." : error, error.Length > 0);
    }

    public void SetConfigFile(string path)
    {
        var error = State.SetConfigFile(path);
        ShowMessage(error.Length == 0 ? "Config file saved." : error, error.Length > 0);
    }

    public void UseAutoDetect()
    {
        State.Settings.PortExecutable = null;
        State.Settings.GameFolder = null;
        State.Settings.ConfigFile = null;
        State.Settings.Save();
        State.Refresh();
        ShowMessage("Searching the usual locations again.", false);
    }

    private async Task PlayAsync()
    {
        ShowMessage("Launching the game…", false);
        var (success, message) = await State.LaunchAsync();
        ShowMessage(message, !success);
        Refresh();
    }

    public async Task SetupNativePortAsync()
    {
        var storage = Avalonia.Controls.TopLevel.GetTopLevel(
            Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null)?.StorageProvider;
        if (storage is null)
        {
            ShowMessage("A folder picker is unavailable in this desktop session.", true);
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose your extracted SMNE01 game folder (Dolphin's Extract Entire Disc)",
            AllowMultiple = false,
        });

        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { Length: > 0 } path)
        {
            return;
        }

        var validation = PortService.ValidateGameFolder(path);
        if (!validation.IsValid)
        {
            ShowMessage(validation.Error ?? "Choose a valid extracted game folder.", true);
            return;
        }

        _setupCancellation = new CancellationTokenSource();
        IsSettingUp = true;
        SetupStatus = "Starting automatic setup...";
        ShowMessage(
            "PipeWizard will clone the port source, install the .NET 8 SDK into your user data, " +
            "download its pinned compiler toolchain, and build WiiCompiled. Game data is not downloaded.",
            false);

        var progress = new Progress<string>(message => SetupStatus = message);
        try
        {
            var service = new NativePortSetupService();
            var executable = await service.InstallAndBuildAsync(
                path, progress, _setupCancellation.Token);
            var error = State.ConfigurePortAndGame(executable, path);
            if (error.Length > 0)
            {
                ShowMessage(error, true);
                return;
            }

            SetupStatus = "Setup complete.";
            ShowMessage("The native port was built and Config.toml is ready.", false);
        }
        catch (OperationCanceledException)
        {
            SetupStatus = "Setup cancelled.";
            ShowMessage("Automatic setup was cancelled. You can retry from Home.", false);
        }
        catch (Exception exception)
        {
            SetupStatus = "Setup stopped.";
            Log.Error("Automatic native-port setup failed.", exception);
            ShowMessage($"Automatic setup failed: {exception.Message}", true);
        }
        finally
        {
            _setupCancellation?.Dispose();
            _setupCancellation = null;
            IsSettingUp = false;
            State.Refresh();
            Refresh();
        }
    }

    public void CancelSetup()
    {
        _setupCancellation?.Cancel();
    }

    private void OpenConfig() => Open(File.Exists(State.ConfigFile)
        ? State.ConfigFile
        : Path.GetDirectoryName(State.ConfigFile) ?? AppPaths.DataDirectory);

    private void OpenGameFolder()
    {
        if (string.IsNullOrWhiteSpace(State.GameFolder))
        {
            ShowMessage("Choose an extracted game folder first.", true);
            return;
        }

        Open(State.GameFolder);
    }

    private void Open(string path)
    {
        var error = PlatformService.OpenPath(path);
        if (error is not null)
        {
            ShowMessage(error, true);
        }
    }
}
