using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CosmicTranscriber;

internal static class Program
{
    private static int _checks, _failures;
    [STAThread]
    public static int Main()
    {
        // Production App methods and real BAML palettes/styles; no App.xaml Startup event,
        // MainWindow code-behind, profile service, network service or visible window.
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.Dark };
        var load = typeof(App).GetMethod("LoadApplicationResources", BindingFlags.Instance | BindingFlags.NonPublic)!;
        load.Invoke(app, null);
        app.ApplyTheme("Cosmic Night");
        Dump(app, "initialized");
        var root = new Border();
        root.SetResourceReference(Border.BackgroundProperty, "WindowGradientBrush");
        var card = new Border();
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        var text = new TextBlock { Text = "Synthetic theme regression" };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        card.Child = text;
        root.Child = card;
        // Application invalidates resource references on its Window trees, not
        // arbitrary detached elements. Register a real logical tree, never Show it.
        var host = new Window { Content = root, Width = 320, Height = 160 };
        root.Measure(new Size(320, 160));
        root.Arrange(new Rect(0, 0, 320, 160));
        root.UpdateLayout();
        var sentinel = new ResourceDictionary { ["UnrelatedThemeSentinel"] = "preserved" };
        app.Resources.MergedDictionaries.Add(sentinel);

        foreach (var theme in new[] { "Cosmic Paper", "Cosmic Night", "Cosmic Paper", "Cosmic Paper", "Cosmic Night" })
            Verify(app, theme, root, card, text, sentinel);

        // Saved Paper at startup must also switch back live, without a second app instance.
        load.Invoke(app, null);
        app.ApplyTheme("Cosmic Paper");
        app.Resources.MergedDictionaries.Add(sentinel);
        Verify(app, "Cosmic Night", root, card, text, sentinel);
        Verify(app, "cOsMiC pApEr", root, card, text, sentinel);
        Verify(app, null, root, card, text, sentinel);
        Check(!host.IsVisible && PresentationSource.FromVisual(root) is null, "host never shown or connected to a native source");
        var transitions = CosmicTranscriber.Utilities.StartupDiagnostics.ThemeTransitions;
        Check(transitions.Count == 20 && transitions.GroupBy(t => t.Id).All(g => g.Key != Guid.Empty &&
            g.Select(t => t.Stage).SequenceEqual(new[] { "requested", "applied" })), "actual App emits correlated requested/applied stages per operation");
        Check(transitions.All(t => t.Requested is "Cosmic Night" or "Cosmic Paper" &&
            t.Previous is "Cosmic Night" or "Cosmic Paper" && t.Failure is null && t.Elapsed >= 0) &&
            transitions.Where(t => t.Stage == "applied").All(t => t.Index >= 0 && t.Index < t.Count),
            "actual App emits normalized palette context and valid owned dictionary index");
        Console.WriteLine($"ThemeRegression: {_checks} checks, {_failures} failures; no visible windows or profile/provider calls.");
        return _failures == 0 ? 0 : 1;
    }

    private static void Verify(App app, string? requested, Border root, Border card, TextBlock text, ResourceDictionary sentinel)
    {
        app.ApplyTheme(requested);
        var paper = string.Equals(requested, "Cosmic Paper", StringComparison.OrdinalIgnoreCase);
        var file = paper ? "CosmicPaper" : "CosmicNight";
        var expected = new ResourceDictionary { Source = new Uri($"Themes/{file}.xaml", UriKind.Relative) };
        Check(app.CurrentTheme == (paper ? "Cosmic Paper" : "Cosmic Night"), "current theme");
        Check(app.ThemeMode == (paper ? ThemeMode.Light : ThemeMode.Dark), "Fluent mode");
        foreach (var key in expected.Keys.Cast<object>().Where(k => expected[k] is Color))
            Check(Equals(app.TryFindResource(key), expected[key]), $"{app.CurrentTheme}: resource {key}");
        Check(BrushMatches(root.Background, (Brush)expected["WindowGradientBrush"]), "existing root dynamic gradient");
        Check(BrushMatches(card.Background, (Brush)expected["SurfaceBrush"]), "existing card dynamic brush");
        Check(BrushMatches(text.Foreground, (Brush)expected["TextPrimaryBrush"]), "existing text dynamic brush");
        var palettes = app.Resources.MergedDictionaries.Where(d => d.Source?.OriginalString is "Themes/CosmicNight.xaml" or "Themes/CosmicPaper.xaml").ToArray();
        Check(palettes.Length == 1, "exactly one app palette");
        Check(app.Resources.MergedDictionaries.Count(d => d.Source?.OriginalString == "Themes/SharedStyles.xaml") == 1, "shared styles retained once");
        Check(app.Resources.MergedDictionaries.Contains(sentinel) && Equals(app.TryFindResource("UnrelatedThemeSentinel"), "preserved"), "unrelated dictionary retained");
        Check(app.Resources.MergedDictionaries.Count(d => d.Source?.OriginalString.Contains("PresentationFramework.Fluent", StringComparison.OrdinalIgnoreCase) == true) == 1, "Fluent dictionary retained once");
        Dump(app, app.CurrentTheme);
    }

    private static bool BrushMatches(Brush? actual, Brush expected) => (actual, expected) switch
    {
        (SolidColorBrush a, SolidColorBrush b) => a.Color == b.Color && a.Opacity == b.Opacity,
        (LinearGradientBrush a, LinearGradientBrush b) => a.StartPoint == b.StartPoint && a.EndPoint == b.EndPoint &&
            a.GradientStops.Select(s => (s.Color, s.Offset)).SequenceEqual(b.GradientStops.Select(s => (s.Color, s.Offset))),
        _ => false
    };

    private static void Check(bool ok, string label)
    {
        _checks++;
        if (!ok) { _failures++; Console.WriteLine("FAIL " + label); }
    }
    private static void Dump(App app, string stage) => Console.WriteLine(stage + ": " +
        string.Join(" | ", app.Resources.MergedDictionaries.Select(d => d.Source?.OriginalString ?? "unrelated")));
}
