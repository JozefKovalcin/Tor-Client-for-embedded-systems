using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Tor.Adapters.Windows;
using Tor.Application;
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
            var (offline, unavailable, peerProfile, hostingIdentity) = CreateOfflineService(desktop.Args ?? []);
            var model = new ShellViewModel(new SimulationTaskFeed(), new UiDispatcher(), SettingsStore.ForCurrentUser(),
                offline, unavailable, showRealWhenUnavailable: OperatingSystem.IsWindows(),
                bundledRuntime: Path.Combine(AppContext.BaseDirectory, "tor-runtime"), peerProfile: peerProfile,
                hostingIdentity: hostingIdentity);
            model.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(model.Theme))
                    ApplyTheme(model.Theme);
            };
            ApplyTheme(model.Theme);
            var window = new MainWindow { DataContext = model };
            desktop.MainWindow = window;
            window.Opened += async (_, _) => await model.OpenAsync();
            var closing = false;
            var closeReady = false;
            window.Closing += async (_, args) =>
            {
                if (closeReady) return;
                args.Cancel = true;
                if (closing) return;
                closing = true;
                await Task.Yield();
                try { await model.DisposeAsync(); }
                catch (Exception)
                {
                    model.ReportCloseFailure();
                    closeReady = true;
                    return;
                }
                closeReady = true;
                window.Close();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static (IOfflineTorService? Service, string? Error, PeerProfileStore? PeerProfile, HostedOnionStore? HostingIdentity)
        CreateOfflineService(string[] args)
    {
        if (!OperatingSystem.IsWindows())
            return (null, "Real Offline requires Windows. Demo uses simulated data.", null, null);
        try
        {
            var options = OfflineLaunchOptions.Read(args);
            var runtime = WindowsRuntimeSelection.ForCurrentUser().ReadRuntime(options.PackageDirectory, !options.ExplicitPackage);
            return (OfflineTorService.CreateWindows(runtime, options.PrivateDirectory), null,
                new PeerProfileStore(options.PrivateDirectory), new HostedOnionStore(options.PrivateDirectory));
        }
        catch (ArgumentException)
        {
            return (null, "Use absolute folders with --tor-package and --state-dir. Keep private state separate from the installation and runtime.", null, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or InvalidDataException or System.Text.Json.JsonException
            or System.Security.SecurityException)
        {
            return (null, "Runtime or private workspace verification failed. For a changed approved build, use Builds to roll back and restart.", null, null);
        }
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
