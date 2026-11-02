using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PipeWizard.ViewModels;

namespace PipeWizard.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private async void PickPort(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel viewModel)
        {
            return;
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Locate the WiiCompiled executable",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("WiiCompiled")
                {
                    Patterns = new[] { "WiiCompiled", "WiiCompiled.exe", "WiiCompiled*" },
                },
                FilePickerFileTypes.All,
            },
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { Length: > 0 } path)
        {
            viewModel.SetPortExecutable(path);
        }
    }

    private async void PickGameFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel viewModel)
        {
            return;
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the folder you extracted with Dolphin",
            AllowMultiple = false,
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { Length: > 0 } path)
        {
            viewModel.SetGameFolder(path);
        }
    }
}
