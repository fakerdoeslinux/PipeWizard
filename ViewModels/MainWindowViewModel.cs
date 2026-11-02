using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PipeWizard.Models;
using PipeWizard.Services;

namespace PipeWizard.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private int _navIndex;
    private ObservableObject _currentPage;

    public AppState State { get; }
    public HomeViewModel Home { get; }
    public ModsViewModel Mods { get; }
    public InstalledViewModel Installed { get; }
    public SettingsViewModel SettingsPage { get; }

    public MainWindowViewModel(AppState state)
    {
        State = state;
        Home = new HomeViewModel(this);
        Mods = new ModsViewModel(this);
        Installed = new InstalledViewModel(this);
        SettingsPage = new SettingsViewModel(this);
        _currentPage = Home;
        NavigateCommand = new RelayCommand(parameter => Navigate(parameter as string ?? "home"));

        state.Changed += (_, _) => OnStateChanged();
        state.LaunchStateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsGameRunning));
            OnPropertyChanged(nameof(PlayLabel));
            OnPropertyChanged(nameof(PlayEnabled));
            Home.Refresh();
        };
    }

    public ObservableObject CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public int SelectedNavIndex
    {
        get => _navIndex;
        set
        {
            if (!SetProperty(ref _navIndex, value))
            {
                return;
            }

            CurrentPage = value switch
            {
                1 => Mods,
                2 => Installed,
                3 => SettingsPage,
                _ => Home,
            };

            if (value == 1 && !Mods.HasLoaded)
            {
                _ = Mods.LoadAsync(1);
            }

            if (value == 2)
            {
                Installed.Refresh();
            }

            if (value == 3)
            {
                SettingsPage.Reload();
            }
        }
    }

    public string GameStatusText => State.IsGameReady
        ? "Ready to play"
        : State.IsPortReady
            ? "Game folder needed"
            : "Port not found";

    public string GameStatusDetail => State.IsGameReady
        ? $"Revision {State.Game.Revision} · {State.EnabledModCount} mod{(State.EnabledModCount == 1 ? string.Empty : "s")} enabled"
        : State.IsPortReady
            ? "Point PipeWizard at your extracted disc"
            : "Locate the WiiCompiled executable";

    public bool StatusOk => State.IsGameReady;
    public bool IsGameRunning => State.IsGameRunning;
    public bool PlayEnabled => State.CanPlay;
    public string PlayLabel => State.IsGameRunning ? "Running" : "Play";

    public ICommand NavigateCommand { get; }

    public void Navigate(string page)
    {
        SelectedNavIndex = page switch
        {
            "mods" => 1,
            "installed" => 2,
            "settings" => 3,
            _ => 0,
        };
    }

    public void OnStateChanged()
    {
        Home.Refresh();
        Installed.Refresh();
        Mods.RefreshInstalled();
        OnPropertyChanged(nameof(GameStatusText));
        OnPropertyChanged(nameof(GameStatusDetail));
        OnPropertyChanged(nameof(StatusOk));
        OnPropertyChanged(nameof(PlayEnabled));
        OnPropertyChanged(nameof(PlayLabel));
    }

    public void ShowHomeMessage(string message, bool isError)
    {
        Home.ShowMessage(message, isError);
    }
}
