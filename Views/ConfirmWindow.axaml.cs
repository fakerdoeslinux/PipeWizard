using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PipeWizard.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
        : this("Are you sure?", "This action cannot be undone.", "Confirm")
    {
    }

    public ConfirmWindow(string header, string message, string confirmLabel = "Confirm")
    {
        InitializeComponent();
        DataContext = new ConfirmInfo(header, message, confirmLabel);
    }

    private void Confirm(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel(object? sender, RoutedEventArgs e) => Close(false);
}

public sealed record ConfirmInfo(string Header, string Message, string ConfirmLabel);
