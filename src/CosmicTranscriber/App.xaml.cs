using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using CosmicTranscriber.Utilities;

namespace CosmicTranscriber;

public partial class App : Application
{
    private ResourceDictionary? _paletteDictionary;

    public App()
    {
        StartupDiagnostics.Initialize();
        StartupDiagnostics.WriteLifecycle("constructor_entered");

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    public string CurrentTheme { get; private set; } = "Cosmic Night";

    private void App_Startup(object sender, StartupEventArgs e)
    {
        try
        {
            StartupDiagnostics.WriteLifecycle("resources_loading");
            LoadApplicationResources();

            StartupDiagnostics.WriteLifecycle("main_window_constructing");
            var window = new MainWindow();
            MainWindow = window;

            StartupDiagnostics.WriteLifecycle("window_showing");
            window.Show();
            window.Activate();
            var dpi = VisualTreeHelper.GetDpi(window);
            StartupDiagnostics.WriteWindow("main_window_shown", window.IsVisible, window.IsActive, window.WindowState.ToString(), window.ActualWidth, window.ActualHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY);
        }
        catch (Exception ex)
        {
            HandleFatalStartupException(ex);
        }
    }

    private void App_Exit(object sender, ExitEventArgs e)
        => StartupDiagnostics.Shutdown(e.ApplicationExitCode);

    public void ApplyTheme(string? requestedTheme)
    {
        var isPaper = string.Equals(requestedTheme, "Cosmic Paper", StringComparison.OrdinalIgnoreCase);
        var dictionaryPath = isPaper ? "Themes/CosmicPaper.xaml" : "Themes/CosmicNight.xaml";
        var nextTheme = isPaper ? "Cosmic Paper" : "Cosmic Night";
        var previousTheme = CurrentTheme;
        var operationId = Guid.NewGuid();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int PaletteIndex() => _paletteDictionary is null ? -1 : Resources.MergedDictionaries.IndexOf(_paletteDictionary);
        StartupDiagnostics.WriteThemeTransition(operationId, "requested", nextTheme, previousTheme,
            Resources.MergedDictionaries.Count, PaletteIndex(), 0);
        try
        {
            var nextPalette = LoadResourceDictionary(dictionaryPath);
            // Fluent ThemeMode can insert its own dictionary at index zero. Replace
            // only the palette we own, wherever it currently sits in the collection.
            var paletteIndex = _paletteDictionary is null
                ? -1
                : Resources.MergedDictionaries.IndexOf(_paletteDictionary);
            if (paletteIndex < 0)
            {
                Resources.MergedDictionaries.Add(nextPalette);
            }
            else
            {
                Resources.MergedDictionaries[paletteIndex] = nextPalette;
            }
            _paletteDictionary = nextPalette;

    #pragma warning disable WPF0001
            ThemeMode = isPaper ? System.Windows.ThemeMode.Light : System.Windows.ThemeMode.Dark;
    #pragma warning restore WPF0001

            CurrentTheme = nextTheme;
            StartupDiagnostics.WriteThemeTransition(operationId, "applied", nextTheme, previousTheme,
                Resources.MergedDictionaries.Count, PaletteIndex(), clock.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteThemeTransition(operationId, "failed", nextTheme, previousTheme,
                Resources.MergedDictionaries.Count, PaletteIndex(), clock.Elapsed.TotalMilliseconds, ex);
            throw;
        }
    }

    private void LoadApplicationResources()
    {
        Resources.MergedDictionaries.Clear();
        _paletteDictionary = LoadResourceDictionary("Themes/CosmicNight.xaml");
        Resources.MergedDictionaries.Add(_paletteDictionary);
        Resources.MergedDictionaries.Add(LoadResourceDictionary("Themes/SharedStyles.xaml"));
        CurrentTheme = "Cosmic Night";
    }

    private static ResourceDictionary LoadResourceDictionary(string relativePath)
        => new()
        {
            Source = new Uri(relativePath, UriKind.Relative)
        };

    private void HandleFatalStartupException(Exception exception)
    {
        StartupDiagnostics.WriteException("FATAL STARTUP", exception);
        var logPath = StartupDiagnostics.LogPath;
        var details = string.IsNullOrWhiteSpace(logPath)
            ? "No writable diagnostic-log location was available."
            : $"Diagnostic log:\n{logPath}";

        try
        {
            MessageBox.Show(
                $"Cosmic Transcriber could not start.\n\n{exception.GetType().Name}: {exception.Message}\n\n{details}",
                "Cosmic Transcriber startup failure",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // The file log remains the authoritative fallback if UI error reporting fails.
        }

        Shutdown(-10);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        StartupDiagnostics.WriteException("UNHANDLED DISPATCHER", e.Exception);
        try
        {
            MessageBox.Show(
                $"Cosmic Transcriber encountered an unrecoverable error and will close.\n\n" +
                $"{e.Exception.GetType().Name}: {e.Exception.Message}\n\nDiagnostic log:\n{StartupDiagnostics.LogPath}",
                "Cosmic Transcriber error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
        }

        e.Handled = true;
        Shutdown(-20);
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            StartupDiagnostics.WriteException("UNHANDLED APPDOMAIN", exception);
        }
        else
        {
            StartupDiagnostics.WriteLifecycle("unhandled_non_exception");
        }
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        StartupDiagnostics.WriteException("UNOBSERVED TASK", e.Exception);
        e.SetObserved();
    }
}
