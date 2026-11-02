using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using PipeWizard.Models;
using PipeWizard.Services;

namespace PipeWizard.ViewModels;

public sealed class InstalledViewModel : ObservableObject
{
    private readonly MainWindowViewModel _shell;
    private string _statusMessage = string.Empty;
    private bool _statusIsError;

    public InstalledViewModel(MainWindowViewModel shell)
    {
        _shell = shell;
        BrowseModsCommand = new RelayCommand(() => _shell.Navigate("mods"));
        OpenModsFolderCommand = new RelayCommand(() => Open(AppPaths.ModsDirectory));
    }

    private AppState State => _shell.State;

    public event EventHandler<InstalledModRowViewModel>? RemoveRequested;

    public ObservableCollection<InstalledModRowViewModel> Mods { get; } = new();

    public bool HasMods => Mods.Count > 0;
    public bool IsEmpty => !HasMods;

    public string CountText
    {
        get
        {
            if (Mods.Count == 0)
            {
                return "No mods installed yet";
            }

            var enabled = Mods.Count(row => row.IsEnabled);
            return $"{Mods.Count} installed · {enabled} enabled";
        }
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

    public ICommand BrowseModsCommand { get; }
    public ICommand OpenModsFolderCommand { get; }

    public void Refresh()
    {
        var previousStatus = StatusMessage;
        var previousError = StatusIsError;

        Mods.Clear();
        var source = State.InstalledMods
            .OrderBy(mod => mod.Enabled ? 0 : 1)
            .ThenBy(mod => mod.Priority)
            .ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var enabled = source.Where(mod => mod.Enabled).ToList();
        foreach (var mod in source)
        {
            var row = new InstalledModRowViewModel(this, mod);
            var index = enabled.IndexOf(mod);
            row.CanMoveUp = mod.Enabled && index > 0;
            row.CanMoveDown = mod.Enabled && index >= 0 && index < enabled.Count - 1;
            Mods.Add(row);
        }

        OnPropertyChanged(nameof(HasMods));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));

        StatusMessage = previousStatus;
        StatusIsError = previousError;
    }

    internal void SetEnabled(InstalledModRowViewModel row, bool enabled)
    {
        var error = State.SetModEnabled(row.Model, enabled);
        if (error.Length > 0)
        {
            StatusMessage = error;
            StatusIsError = true;
            row.SyncEnabled(!enabled);
            return;
        }

        StatusMessage = enabled
            ? $"{row.Model.Name} enabled. Restart the game to apply it."
            : $"{row.Model.Name} disabled. Restart the game to apply it.";
        StatusIsError = false;
    }

    internal void Move(InstalledModRowViewModel row, int delta)
    {
        var error = State.MoveMod(row.Model, delta);
        if (error.Length > 0)
        {
            StatusMessage = error;
            StatusIsError = true;
        }
        else
        {
            StatusMessage = "Mod priority updated. Restart the game to apply it.";
            StatusIsError = false;
        }
    }

    internal void RequestRemove(InstalledModRowViewModel row) =>
        RemoveRequested?.Invoke(this, row);

    internal void RemoveConfirmed(InstalledModRowViewModel row)
    {
        var name = row.Model.Name;
        var error = State.RemoveMod(row.Model);
        StatusMessage = error.Length > 0 ? error : $"{name} removed.";
        StatusIsError = error.Length > 0;
    }

    internal void OpenFolder(InstalledModRowViewModel row)
    {
        var error = PlatformService.OpenPath(row.Model.InstallDirectory);
        if (error is not null)
        {
            StatusMessage = error;
            StatusIsError = true;
        }
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

    public void ShowError(string message)
    {
        StatusMessage = message;
        StatusIsError = true;
    }
}

public sealed class InstalledModRowViewModel : ObservableObject
{
    private readonly InstalledViewModel _owner;
    private bool _isEnabled;
    private bool _canMoveUp;
    private bool _canMoveDown;

    public InstalledModRowViewModel(InstalledViewModel owner, InstalledMod model)
    {
        _owner = owner;
        Model = model;
        _isEnabled = model.Enabled;
        MoveUpCommand = new RelayCommand(() => _owner.Move(this, -1));
        MoveDownCommand = new RelayCommand(() => _owner.Move(this, 1));
        RemoveCommand = new RelayCommand(() => _owner.RequestRemove(this));
        OpenFolderCommand = new RelayCommand(() => _owner.OpenFolder(this));
    }

    public InstalledMod Model { get; }
    public string Name => Model.Name;
    public string Author => Model.Author;
    public string VersionText => Model.VersionText;
    public string KindLabel => Model.KindLabel;
    public string SizeText => Model.SizeText;
    public string InstalledText => Model.InstalledText;
    public string PriorityText => Model.PriorityText;
    public bool IsRiivolution => Model.IsRiivolution;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (!SetProperty(ref _isEnabled, value))
            {
                return;
            }

            OnPropertyChanged(nameof(PriorityText));
            _owner.SetEnabled(this, value);
        }
    }

    public string PriorityLabel => Model.Enabled
        ? $"#{Model.Priority + 1}"
        : "off";

    public bool CanMoveUp
    {
        get => _canMoveUp;
        set => SetProperty(ref _canMoveUp, value);
    }

    public bool CanMoveDown
    {
        get => _canMoveDown;
        set => SetProperty(ref _canMoveDown, value);
    }

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand OpenFolderCommand { get; }

    public void SyncEnabled(bool enabled)
    {
        _isEnabled = enabled;
        OnPropertyChanged(nameof(IsEnabled));
    }
}
