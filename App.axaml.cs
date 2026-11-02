using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PipeWizard.Services;
using PipeWizard.ViewModels;
using PipeWizard.Views;

namespace PipeWizard;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var appState = new AppState();
            var window = new MainWindow
            {
                DataContext = new MainWindowViewModel(appState),
            };
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
