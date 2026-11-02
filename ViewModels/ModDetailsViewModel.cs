using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using PipeWizard.Models;
using PipeWizard.Services;

namespace PipeWizard.ViewModels;

public sealed class ModFileViewModel
{
    public ModFileViewModel(GbFile file)
    {
        File = file;
    }

    public GbFile File { get; }
    public string Name => File.FileName;
    public string SizeText => PlatformService.FormatSize(File.Size);
    public string VersionText => string.IsNullOrWhiteSpace(File.Version) ? "No version" : File.Version;
    public string DateText => DateTimeOffset.FromUnixTimeSeconds(File.DateAdded).LocalDateTime.ToString("d MMM yyyy");
    public bool IsArchived => File.IsArchived;
    public string BadgeText => IsArchived ? "older" : "current";
}

public sealed class ModDetailsViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly ModSummary _summary;
    private string _description = string.Empty;
    private string _versionText = string.Empty;
    private string _statsText = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _statusIsError;
    private bool _isLoading = true;
    private string _loadError = string.Empty;
    private bool _isInstalling;
    private double _progressValue;
    private string _progressText = string.Empty;
    private ModFileViewModel? _selectedFile;
    private bool _isInstalled;

    public ModDetailsViewModel(AppState state, ModSummary summary)
    {
        _state = state;
        _summary = summary;
        Name = summary.Name;
        Author = summary.Author;
        Category = summary.Category;
        AddedText = $"Added {summary.AddedText}";
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => CanInstall);
        OpenPageCommand = new RelayCommand(OpenPage);
        RetryCommand = new AsyncRelayCommand(() => LoadAsync(forceRefresh: true));
        IsInstalled = state.InstalledMods.Any(mod => mod.ModId == summary.Id);
    }

    public string Name { get; }
    public string Author { get; }
    public string Category { get; }
    public string AddedText { get; }
    public string ProfileUrl => _summary.ProfileUrl;

    public ObservableCollection<Bitmap> Images { get; } = new();
    public ObservableCollection<ModFileViewModel> Files { get; } = new();

    public string Description
    {
        get => _description;
        private set => SetProperty(ref _description, value);
    }

    public string VersionText
    {
        get => _versionText;
        private set => SetProperty(ref _versionText, value);
    }

    public string StatsText
    {
        get => _statsText;
        private set => SetProperty(ref _statsText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(ShowContent));
                OnPropertyChanged(nameof(CanInstall));
                RaiseInstallCanExecute();
            }
        }
    }

    public string LoadError
    {
        get => _loadError;
        private set
        {
            if (SetProperty(ref _loadError, value))
            {
                OnPropertyChanged(nameof(HasLoadError));
                OnPropertyChanged(nameof(ShowContent));
            }
        }
    }

    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);
    public bool ShowContent => !IsLoading && !HasLoadError;

    public bool IsInstalling
    {
        get => _isInstalling;
        private set
        {
            if (SetProperty(ref _isInstalling, value))
            {
                OnPropertyChanged(nameof(CanInstall));
                OnPropertyChanged(nameof(ShowProgress));
                RaiseInstallCanExecute();
            }
        }
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public bool ShowProgress => IsInstalling;

    public bool IsInstalled
    {
        get => _isInstalled;
        private set
        {
            if (SetProperty(ref _isInstalled, value))
            {
                OnPropertyChanged(nameof(InstallButtonText));
                OnPropertyChanged(nameof(InstalledText));
                OnPropertyChanged(nameof(ShowInstalledNote));
            }
        }
    }

    public string InstallButtonText => IsInstalled ? "Reinstall mod" : "Install mod";
    public string InstalledText => IsInstalled ? "Installed and ready" : string.Empty;
    public bool ShowInstalledNote => IsInstalled;

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

    public ModFileViewModel? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetProperty(ref _selectedFile, value))
            {
                OnPropertyChanged(nameof(CanInstall));
                RaiseInstallCanExecute();
            }
        }
    }

    public bool CanInstall => !IsLoading && !IsInstalling && SelectedFile is not null;

    public ICommand InstallCommand { get; }
    public ICommand OpenPageCommand { get; }
    public ICommand RetryCommand { get; }

    public async Task LoadAsync(bool forceRefresh = false)
    {
        IsLoading = true;
        LoadError = string.Empty;

        try
        {
            var details = await _state.GameBanana.GetModDetailsAsync(_summary.Id, forceRefresh);
            Description = HtmlText.ToPlainText(details.Text);
            if (string.IsNullOrWhiteSpace(Description))
            {
                Description = "The author did not provide a description.";
            }

            VersionText = string.IsNullOrWhiteSpace(details.Version) ? "No version" : details.Version;
            StatsText = $"{details.ViewCount:N0} views · {details.LikeCount:N0} likes · {details.DownloadCount:N0} downloads";

            var files = (details.Files ?? new List<GbFile>())
                .OrderBy(file => file.IsArchived ? 1 : 0)
                .ThenByDescending(file => file.DateAdded)
                .ToList();

            Files.Clear();
            foreach (var file in files)
            {
                Files.Add(new ModFileViewModel(file));
            }

            var installedFileId = _state.InstalledMods
                .FirstOrDefault(mod => mod.ModId == _summary.Id)?.FileId;
            SelectedFile = Files.FirstOrDefault(file => file.File.Id == installedFileId) ?? Files.FirstOrDefault();

            var images = details.PreviewMedia?.Images?
                .Select(image => image.ThumbnailUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct()
                .Take(4)
                .ToList() ?? new List<string?>();

            Images.Clear();
            foreach (var url in images)
            {
                var bitmap = await _state.GameBanana.GetImageAsync(url);
                if (bitmap is not null)
                {
                    Images.Add(bitmap);
                }
            }

            IsInstalled = _state.InstalledMods.Any(mod => mod.ModId == _summary.Id);
        }
        catch (Exception exception)
        {
            LoadError = $"Could not load this mod from GameBanana: {exception.Message}";
            Log.Error($"Could not load mod {_summary.Id}.", exception);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task InstallAsync()
    {
        if (SelectedFile is null)
        {
            return;
        }

        IsInstalling = true;
        StatusIsError = false;
        StatusMessage = "Starting install…";
        ProgressValue = 0;
        ProgressText = "Preparing";

        try
        {
            var progress = new Progress<InstallProgress>(update =>
            {
                ProgressValue = update.Fraction * 100;
                ProgressText = update.Stage;
            });

            var result = await _state.InstallAsync(_summary, SelectedFile.File, progress);
            StatusMessage = result.Message;
            StatusIsError = !result.Success;
            if (result.Mod is not null)
            {
                IsInstalled = true;
            }

            ProgressValue = result.Success ? 100 : ProgressValue;
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private void OpenPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_summary.ProfileUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not open the browser: {exception.Message}";
            StatusIsError = true;
        }
    }

    private void RaiseInstallCanExecute()
    {
        if (InstallCommand is AsyncRelayCommand command)
        {
            command.RaiseCanExecuteChanged();
        }
    }
}
