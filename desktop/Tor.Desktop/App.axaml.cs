using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using TorManager.Desktop.Presentation;
using TorManager.Desktop.Services;

namespace TorManager.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var model = new ShellViewModel(new SimulationTaskFeed(), new UiDispatcher(), SettingsStore.ForCurrentUser());
            model.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(model.Theme))
                    ApplyTheme(model.Theme);
            };
            ApplyTheme(model.Theme);
            desktop.MainWindow = new MainWindow { DataContext = model };
            desktop.MainWindow.Opened += (_, _) => model.ReplayCommand.Execute(null);
            desktop.MainWindow.Closed += (_, _) => model.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyTheme(string theme) => RequestedThemeVariant = theme switch
    {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default
    };

    private sealed class UiDispatcher : IUiDispatcher
    {
        public void Post(Action update) => Dispatcher.UIThread.Post(update, DispatcherPriority.Background);
    }
}
