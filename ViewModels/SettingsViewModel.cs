using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using PipeWizard.Services;

namespace PipeWizard.ViewModels;

public sealed record ResolutionOption(string Label, double Value)
{
    public override string ToString() => Label;
}

public sealed class SettingsViewModel : ObservableObject
{
    private readonly MainWindowViewModel _shell;
    private bool _widescreen = true;
    private ResolutionOption _selectedResolution;
    private bool _showFps;
    private string _displayMode = "windowed";
    private string _graphicsApi = "auto";
    private decimal? _windowWidth = 854;
    private decimal? _windowHeight = 480;
    private bool _mangoHud;
    private string _statusMessage = string.Empty;
    private bool _statusIsError;
    private string _configStatus = string.Empty;

    public SettingsViewModel(MainWindowViewModel shell)
    {
        _shell = shell;

        ResolutionOptions = new ObservableCollection<ResolutionOption>
        {
            new("Window resolution (0×)", 0),
            new("0.5× — 240 lines", 0.5),
            new("0.75× — 360 lines", 0.75),
            new("1× — 480 lines", 1),
            new("1.5× — 720 lines", 1.5),
            new("2× — 960 lines", 2),
            new("3× — 1440 lines", 3),
            new("4× — 1920 lines", 4),
        };
        _selectedResolution = ResolutionOptions[3];

        DisplayModes = new ObservableCollection<string> { "windowed", "borderless", "exclusive" };
        GraphicsApis = new ObservableCollection<string>(
            OperatingSystem.IsWindows()
                ? new[] { "auto", "vulkan", "d3d12", "d3d11" }
                : new[] { "auto", "vulkan" });

        SaveCommand = new RelayCommand(Save);
        OpenConfigFolderCommand = new RelayCommand(OpenConfigFolder);
        OpenDataFolderCommand = new RelayCommand(() => Open(AppPaths.DataDirectory));
        AutoDetectCommand = new RelayCommand(AutoDetect);

        Reload();
    }

    private AppState State => _shell.State;

    public ObservableCollection<ResolutionOption> ResolutionOptions { get; }
    public ObservableCollection<string> DisplayModes { get; }
    public ObservableCollection<string> GraphicsApis { get; }

    public string PortPath => State.PortExecutable ?? "Not found";
    public string GamePath => State.GameFolder ?? "Not set";
    public string ConfigPath => State.ConfigFile;

    public string ConfigStatus
    {
        get => _configStatus;
        private set => SetProperty(ref _configStatus, value);
    }

    public bool Widescreen
    {
        get => _widescreen;
        set => SetProperty(ref _widescreen, value);
    }

    public ResolutionOption SelectedResolution
    {
        get => _selectedResolution;
        set => SetProperty(ref _selectedResolution, value);
    }

    public bool ShowFps
    {
        get => _showFps;
        set => SetProperty(ref _showFps, value);
    }

    public string DisplayMode
    {
        get => _displayMode;
        set => SetProperty(ref _displayMode, value);
    }

    public string GraphicsApi
    {
        get => _graphicsApi;
        set => SetProperty(ref _graphicsApi, value);
    }

    public decimal? WindowWidth
    {
        get => _windowWidth;
        set => SetProperty(ref _windowWidth, value);
    }

    public decimal? WindowHeight
    {
        get => _windowHeight;
        set => SetProperty(ref _windowHeight, value);
    }

    public bool MangoHud
    {
        get => _mangoHud;
        set => SetProperty(ref _mangoHud, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

    public ICommand SaveCommand { get; }
    public ICommand OpenConfigFolderCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand AutoDetectCommand { get; }

    public void Reload()
    {
        var config = State.Config;

        Widescreen = config.GetBool("video", "widescreen") ?? true;
        ShowFps = config.GetBool("video", "show_fps") ?? false;

        var multiplier = config.GetNumber("video", "resolution_multiplier") ?? 1;
        var resolution = ResolutionOptions.FirstOrDefault(option => Math.Abs(option.Value - multiplier) < 0.001);
        if (resolution is null)
        {
            resolution = new ResolutionOption($"Custom ({multiplier:0.##}×)", multiplier);
            ResolutionOptions.Add(resolution);
        }

        SelectedResolution = resolution;

        DisplayMode = config.GetString("video", "display_mode") ?? "windowed";
        if (!DisplayModes.Contains(DisplayMode))
        {
            DisplayModes.Add(DisplayMode);
        }

        GraphicsApi = config.GetString("video", "graphics_api") ?? "auto";
        if (!GraphicsApis.Contains(GraphicsApi))
        {
            GraphicsApis.Add(GraphicsApi);
        }

        WindowWidth = (decimal)(config.GetNumber("video", "window_width") ?? 854);
        WindowHeight = (decimal)(config.GetNumber("video", "window_height") ?? 480);
        MangoHud = State.Settings.UseMangoHud;
        ConfigStatus = File.Exists(State.ConfigFile) ? "Using this file" : "Will be created on save";

        OnPropertyChanged(nameof(PortPath));
        OnPropertyChanged(nameof(GamePath));
        OnPropertyChanged(nameof(ConfigPath));
        OnPropertyChanged(nameof(ConfigStatus));
    }

    public void SetPortExecutable(string path)
    {
        var error = State.SetPortExecutable(path);
        Reload();
        StatusMessage = error.Length == 0 ? "Port location saved." : error;
        StatusIsError = error.Length > 0;
    }

    public void SetGameFolder(string path)
    {
        var error = State.SetGameFolder(path);
        Reload();
        StatusMessage = error.Length == 0 ? "Game folder saved." : error;
        StatusIsError = error.Length > 0;
    }

    public void SetConfigFile(string path)
    {
        var error = State.SetConfigFile(path);
        Reload();
        StatusMessage = error.Length == 0 ? "Config file saved." : error;
        StatusIsError = error.Length > 0;
    }

    private void Save()
    {
        var config = State.Config;

        config.SetBool("video", "widescreen", Widescreen);
        config.SetBool("video", "show_fps", ShowFps);
        config.SetNumber("video", "resolution_multiplier", SelectedResolution.Value);
        config.SetString("video", "display_mode", DisplayMode);
        config.SetString("video", "graphics_api", GraphicsApi);

        if (WindowWidth is > 0)
        {
            config.SetNumber("video", "window_width", (double)WindowWidth.Value);
        }

        if (WindowHeight is > 0)
        {
            config.SetNumber("video", "window_height", (double)WindowHeight.Value);
        }

        if (State.IsGameReady && State.GameFolder is not null)
        {
            config.SetString("paths", "dvd_root", State.GameFolder);
        }

        State.Settings.UseMangoHud = MangoHud;
        State.Settings.Save();

        var error = State.SaveConfig();
        State.Refresh();
        Reload();

        if (error is not null)
        {
            StatusMessage = error;
            StatusIsError = true;
            return;
        }

        StatusMessage = "Saved to Config.toml. Restart the game to apply video changes.";
        StatusIsError = false;
    }

    private void OpenConfigFolder() =>
        Open(File.Exists(State.ConfigFile)
            ? State.ConfigFile
            : Path.GetDirectoryName(State.ConfigFile) ?? AppPaths.DataDirectory);

    private void AutoDetect()
    {
        State.Settings.PortExecutable = null;
        State.Settings.GameFolder = null;
        State.Settings.ConfigFile = null;
        State.Settings.Save();
        State.Refresh();
        Reload();
        StatusMessage = "Searched the usual build and game folders again.";
        StatusIsError = false;
    }

    private void Open(string path)
    {
        var error = PlatformService.OpenPath(path);
        if (error is not null)
        {
            StatusMessage = error;
            StatusIsError = true;
        }
    }
}
