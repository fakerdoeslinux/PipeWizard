using System;
using Avalonia.Controls;
using PipeWizard.Models;
using PipeWizard.Services;
using PipeWizard.ViewModels;

namespace PipeWizard.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.Mods.DetailsRequested -= OnDetailsRequested;
            _viewModel.Installed.RemoveRequested -= OnRemoveRequested;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.Mods.DetailsRequested += OnDetailsRequested;
            _viewModel.Installed.RemoveRequested += OnRemoveRequested;

            if (_viewModel.State.Settings.WindowWidth >= MinWidth &&
                _viewModel.State.Settings.WindowHeight >= MinHeight)
            {
                Width = _viewModel.State.Settings.WindowWidth;
                Height = _viewModel.State.Settings.WindowHeight;
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.State.Settings.WindowWidth = (int)Math.Round(Width);
            _viewModel.State.Settings.WindowHeight = (int)Math.Round(Height);
            _viewModel.State.Settings.Save();
        }

        base.OnClosed(e);
    }

    private async void OnDetailsRequested(object? sender, ModSummary summary)
    {
        if (_viewModel is null)
        {
            return;
        }

        var window = new ModDetailsWindow
        {
            DataContext = new ModDetailsViewModel(_viewModel.State, summary),
        };

        await window.ShowDialog(this);
    }

    private async void OnRemoveRequested(object? sender, InstalledModRowViewModel row)
    {
        if (_viewModel is null)
        {
            return;
        }

        var confirm = new ConfirmWindow(
            "Remove this mod?",
            $"\"{row.Name}\" will be deleted from {AppPaths.ModsDirectory} and taken out of overlay_roots. " +
            "Your game files are not touched.",
            "Remove mod");

        if (await confirm.ShowDialog<bool>(this))
        {
            _viewModel.Installed.RemoveConfirmed(row);
        }
    }
}
