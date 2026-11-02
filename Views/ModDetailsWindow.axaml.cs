using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PipeWizard.ViewModels;

namespace PipeWizard.Views;

public partial class ModDetailsWindow : Window
{
    public ModDetailsWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is ModDetailsViewModel viewModel)
        {
            _ = viewModel.LoadAsync();
        }
    }

    private void CloseWindow(object? sender, RoutedEventArgs e) => Close();
}
