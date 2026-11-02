using Avalonia.Controls;
using Avalonia.Interactivity;
using PipeWizard.ViewModels;

namespace PipeWizard.Views;

public partial class ModsView : UserControl
{
    public ModsView()
    {
        InitializeComponent();
    }

    private void OpenDetails(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ModsViewModel viewModel &&
            sender is Control { DataContext: ModCardViewModel card })
        {
            viewModel.RequestDetails(card);
        }
    }
}
