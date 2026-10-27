// No actual MainWindow/profile/provider code is linked or invoked by this offscreen test.
namespace CosmicTranscriber
{
    public sealed class MainWindow : System.Windows.Window { }
}

namespace CosmicTranscriber.Utilities
{
    public static class StartupDiagnostics
    {
        public static readonly System.Collections.Generic.List<(System.Guid Id, string Stage, string? Requested,
            string? Previous, int Count, int Index, double Elapsed, System.Exception? Failure)> ThemeTransitions = new();
        public static string LogPath => string.Empty;
        public static void Initialize() { }
        public static void WriteLifecycle(string stage) { }
        public static void Write(string category, string message) { }
        public static void WriteThemeTransition(System.Guid operationId, string stage, string? requestedTheme, string? previousTheme,
            int dictionaryCount, int paletteIndex, double elapsedMs, System.Exception? failure = null)
            => ThemeTransitions.Add((operationId, stage, requestedTheme, previousTheme, dictionaryCount, paletteIndex, elapsedMs, failure));
        public static void WriteException(string category, System.Exception error) { }
        public static void Shutdown(int code) { }
        public static void WriteWindow(string stage, bool visible, bool active, string state,
            double width, double height, double dpiX, double dpiY) { }
    }
}
