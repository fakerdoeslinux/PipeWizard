using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PipeWizard.Models;
using PipeWizard.Services;

namespace PipeWizard.ViewModels;

public sealed record SortOption(string Label, string Value)
{
    public override string ToString() => Label;
}

public sealed class ModsViewModel : ObservableObject
{
    private readonly MainWindowViewModel _shell;
    private readonly DispatcherTimer _searchTimer;
    private CancellationTokenSource? _loadCts;
    private string _searchText = string.Empty;
    private SortOption _selectedSort;
    private bool _isLoading;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private int _totalCount;

    public ModsViewModel(MainWindowViewModel shell)
    {
        _shell = shell;
        SortOptions = new ObservableCollection<SortOption>
        {
            new("Featured", "default"),
            new("Newest", "new"),
            new("Recently updated", "updated"),
        };
        _selectedSort = SortOptions[0];

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            _ = LoadAsync(1);
        };

        PreviousPageCommand = new AsyncRelayCommand(
            () => LoadAsync(CurrentPage - 1),
            () => CurrentPage > 1 && !IsLoading);
        NextPageCommand = new AsyncRelayCommand(
            () => LoadAsync(CurrentPage + 1),
            () => CurrentPage < TotalPages && !IsLoading);
        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(CurrentPage, forceRefresh: true));
        RetryCommand = new AsyncRelayCommand(() => LoadAsync(CurrentPage, forceRefresh: true));
    }

    private GameBananaClient Client => _shell.State.GameBanana;

    public event EventHandler<ModSummary>? DetailsRequested;

    public ObservableCollection<ModCardViewModel> Items { get; } = new();
    public ObservableCollection<SortOption> SortOptions { get; }

    public bool HasLoaded { get; private set; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value))
            {
                return;
            }

            _searchTimer.Stop();
            _searchTimer.Start();
        }
    }

    public SortOption SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (!SetProperty(ref _selectedSort, value) || !HasLoaded)
            {
                return;
            }

            _ = LoadAsync(1);
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(ShowResults));
                OnPropertyChanged(nameof(StatusText));
                RaisePagingChanged();
            }
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (SetProperty(ref _hasError, value))
            {
                OnPropertyChanged(nameof(ShowErrorState));
                OnPropertyChanged(nameof(ShowResults));
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(PageText));
                RaisePagingChanged();
            }
        }
    }

    public int TotalPages
    {
        get => _totalPages;
        private set
        {
            if (SetProperty(ref _totalPages, value))
            {
                OnPropertyChanged(nameof(PageText));
                RaisePagingChanged();
            }
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string PageText => $"Page {CurrentPage} of {Math.Max(1, TotalPages)}";

    public string StatusText
    {
        get
        {
            if (IsLoading)
            {
                return "Loading mods…";
            }

            if (HasError)
            {
                return "Could not reach GameBanana";
            }

            if (TotalCount == 0)
            {
                return "No mods";
            }

            return string.IsNullOrWhiteSpace(SearchText)
                ? $"{TotalCount:N0} mods on GameBanana"
                : $"{TotalCount:N0} result{(TotalCount == 1 ? string.Empty : "s")}";
        }
    }

    public bool ShowResults => !IsLoading && !HasError && Items.Count > 0;
    public bool ShowEmptyState => !IsLoading && !HasError && HasLoaded && Items.Count == 0;
    public bool ShowErrorState => HasError && !IsLoading;

    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand RetryCommand { get; }

    public async Task LoadAsync(int page, bool forceRefresh = false)
    {
        if (page < 1)
        {
            page = 1;
        }

        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            var search = SearchText.Trim();
            GbPage data;
            if (search.Length == 0)
            {
                data = await Client.GetModsAsync(page, SelectedSort.Value, forceRefresh, cts.Token);
            }
            else
            {
                data = await Client.SearchAsync(search, page, forceRefresh, cts.Token);
            }

            if (cts.IsCancellationRequested)
            {
                return;
            }

            var installed = _shell.State.InstalledMods.Select(mod => mod.ModId).ToHashSet();
            Items.Clear();
            foreach (var record in data.Records)
            {
                Items.Add(new ModCardViewModel(ModSummary.From(record), installed.Contains(record.Id)));
            }

            CurrentPage = page;
            TotalCount = data.TotalCount;
            TotalPages = Math.Max(1, (int)Math.Ceiling((double)data.TotalCount / Math.Max(1, data.PerPage)));
            HasLoaded = true;

            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ShowResults));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(ShowErrorState));

            foreach (var card in Items)
            {
                _ = card.LoadImageAsync(Client, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer search or page superseded this one.
        }
        catch (Exception exception)
        {
            if (cts.IsCancellationRequested)
            {
                return;
            }

            Log.Error("Could not load GameBanana mods.", exception);
            ErrorMessage = exception.Message;
            HasError = true;
            OnPropertyChanged(nameof(StatusText));
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                _loadCts = null;
                IsLoading = false;
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(ShowResults));
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(ShowErrorState));
            }

            cts.Dispose();
        }
    }

    public void RefreshInstalled()
    {
        var installed = _shell.State.InstalledMods.Select(mod => mod.ModId).ToHashSet();
        foreach (var card in Items)
        {
            card.IsInstalled = installed.Contains(card.Id);
        }
    }

    public void RequestDetails(ModCardViewModel card) => DetailsRequested?.Invoke(this, card.Summary);

    private void RaisePagingChanged()
    {
        if (PreviousPageCommand is AsyncRelayCommand previous)
        {
            previous.RaiseCanExecuteChanged();
        }

        if (NextPageCommand is AsyncRelayCommand next)
        {
            next.RaiseCanExecuteChanged();
        }
    }
}

public sealed class ModCardViewModel : ObservableObject
{
    private Bitmap? _thumbnail;
    private bool _isInstalled;

    public ModCardViewModel(ModSummary summary, bool isInstalled)
    {
        Summary = summary;
        _isInstalled = isInstalled;
    }

    public ModSummary Summary { get; }

    public int Id => Summary.Id;
    public string Name => Summary.Name;
    public string Author => Summary.Author;
    public string Category => Summary.Category;
    public string VersionText => string.IsNullOrWhiteSpace(Summary.Version) ? string.Empty : Summary.Version;
    public string AddedText => $"Added {Summary.AddedText}";
    public string ViewsText => Summary.ViewsText;
    public string Initial => Name.Length == 0 ? "?" : Name[..1].ToUpperInvariant();

    public bool IsInstalled
    {
        get => _isInstalled;
        set => SetProperty(ref _isInstalled, value);
    }

    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            if (SetProperty(ref _thumbnail, value))
            {
                OnPropertyChanged(nameof(HasThumbnail));
                OnPropertyChanged(nameof(ShowPlaceholder));
            }
        }
    }

    public bool HasThumbnail => Thumbnail is not null;
    public bool ShowPlaceholder => Thumbnail is null;

    public async Task LoadImageAsync(GameBananaClient client, CancellationToken cancellationToken)
    {
        try
        {
            var bitmap = await client.GetImageAsync(Summary.ThumbnailUrl, cancellationToken);
            if (bitmap is not null)
            {
                Thumbnail = bitmap;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
    }
}
