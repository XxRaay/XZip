using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

using XZip.App.Services;
using XZip.App.Pages;
using XZip.App.ViewModels;
using XZip.Core;
using Windows.ApplicationModel.Activation;

namespace XZip.App;

/// <summary>
/// Application root. Bootstraps DI and the main window.
/// </summary>
public partial class App : Application
{
    private Window? _mainWindow;
    private bool _windowActivated;

    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
        UnhandledException += OnUnhandledException;
    }

    public Window MainWindow => _mainWindow ?? throw new InvalidOperationException("MainWindow not yet created.");

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var launchPath = TryGetArchivePathFromActivation() ?? TryGetArchivePathFromLaunchArgs(args.Arguments);
        EnsureWindowAndActivate(launchPath);
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton(_ => ArchiveServiceFactory.CreateDefault());
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<ISettingsService, SettingsService>();

        services.AddSingleton<MainViewModel>();
        services.AddTransient<ArchiveExplorerViewModel>();
        services.AddTransient<CreateArchiveViewModel>();
        services.AddTransient<RecentViewModel>();
        services.AddTransient<SettingsViewModel>();

        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // TODO: hook into logging once a logger is wired into DI.
        System.Diagnostics.Debug.WriteLine($"[XZip] Unhandled exception: {e.Exception}");
    }

    private void EnsureWindowAndActivate(string? archivePath)
    {
        _mainWindow ??= Services.GetRequiredService<MainWindow>();

        if (!_windowActivated)
        {
            _mainWindow.Activate();
            _windowActivated = true;
        }

        var nav = Services.GetRequiredService<INavigationService>();
        if (!string.IsNullOrWhiteSpace(archivePath) && File.Exists(archivePath))
        {
            nav.NavigateTo(typeof(ArchiveExplorerPage), archivePath);
        }
        else if (nav.CurrentPageType is null)
        {
            nav.NavigateTo(typeof(ArchiveExplorerPage));
        }
    }

    private static string? TryGetArchivePathFromLaunchArgs(string? args)
    {
        if (string.IsNullOrWhiteSpace(args)) return null;
        var trimmed = args.Trim();

        // Protocol activation may be forwarded as a launch argument.
        if (trimmed.StartsWith("xzip://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var protocolUri))
            {
                return TryGetArchivePathFromProtocol(protocolUri);
            }
        }

        // Typical shell launch passes the file path (often quoted).
        var candidate = trimmed.Trim('"');
        if (File.Exists(candidate)) return candidate;

        // Fallback: first quoted token, then first space-separated token.
        var firstQuotedStart = trimmed.IndexOf('"');
        if (firstQuotedStart >= 0)
        {
            var secondQuote = trimmed.IndexOf('"', firstQuotedStart + 1);
            if (secondQuote > firstQuotedStart)
            {
                var quoted = trimmed.Substring(firstQuotedStart + 1, secondQuote - firstQuotedStart - 1);
                if (File.Exists(quoted)) return quoted;
            }
        }

        var firstToken = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstToken is not null && File.Exists(firstToken)) return firstToken;

        // Some activation paths are passed via process command line.
        foreach (var token in Environment.GetCommandLineArgs().Skip(1))
        {
            var candidateCmd = token.Trim('"');
            if (File.Exists(candidateCmd)) return candidateCmd;
        }

        return null;
    }

    private static string? TryGetArchivePathFromProtocol(Uri? uri)
    {
        if (uri is null) return null;
        if (!string.Equals(uri.Scheme, "xzip", StringComparison.OrdinalIgnoreCase)) return null;

        var query = uri.Query?.TrimStart('?') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query)) return null;

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            if (!string.Equals(kv[0], "path", StringComparison.OrdinalIgnoreCase)) continue;
            var value = Uri.UnescapeDataString(kv[1]);
            return File.Exists(value) ? value : null;
        }

        return null;
    }

    private static string? TryGetArchivePathFromActivation()
    {
        try
        {
            var activated = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activated is null) return null;

            if (activated.Kind == ExtendedActivationKind.File &&
                activated.Data is IFileActivatedEventArgs fileArgs)
            {
                foreach (var item in fileArgs.Files)
                {
                    if (item is Windows.Storage.StorageFile file && File.Exists(file.Path))
                    {
                        return file.Path;
                    }
                }
            }

            if (activated.Kind == ExtendedActivationKind.Protocol &&
                activated.Data is IProtocolActivatedEventArgs protocolArgs)
            {
                return TryGetArchivePathFromProtocol(protocolArgs.Uri);
            }

            if (activated.Kind == ExtendedActivationKind.Launch &&
                activated.Data is ILaunchActivatedEventArgs launchArgs)
            {
                return TryGetArchivePathFromLaunchArgs(launchArgs.Arguments);
            }
        }
        catch
        {
            // Best-effort activation parse.
        }

        return null;
    }
}
