using Avalonia.Controls;
using Avalonia.Interactivity;
using PipeWizard.ViewModels;
using PipeWizard.Services;

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

    private void OpenFeaturedWip(object? sender, RoutedEventArgs e)
    {
        var error = PlatformService.OpenPath("https://gamebanana.com/wips/65286");
        if (error is not null && DataContext is ModsViewModel viewModel)
        {
            viewModel.ShowExternalLinkError(error);
        }
    }
}
