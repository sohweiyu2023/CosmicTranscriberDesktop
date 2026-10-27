using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using CosmicTranscriber.Models;

internal static class Program
{
    private static readonly HashSet<string> Events = ["Loaded", "Closing", "GotKeyboardFocus", "PreviewKeyDown", "Click", "PasswordChanged", "TextChanged", "SelectionChanged", "PreviewMouseWheel", "ValueChanged", "SizeChanged", "ScrollChanged"];
    private static readonly HashSet<string> Meaningful = ["Button", "TextBlock", "TextBox", "PasswordBox", "ComboBox", "Slider", "CheckBox", "ListView", "Expander"];
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    [STAThread]
    public static int Main(string[] args)
    {
        var source = Path.GetFullPath(args[0]);
        var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        var theme = args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : "CosmicNight";
        var assertReachable = args.Contains("--assert-reachable");
        var quick = args.Contains("--quick");
        var diagnosticCase = args.Contains("--diagnostic-case");
        var singleReveal = args.Contains("--single-reveal");
        var seedDiagnosticScroll = args.Contains("--seed-diagnostic-scroll");
        var repeatArgument = args.FirstOrDefault(a => a.StartsWith("--repeat=", StringComparison.Ordinal));
        var repeats = repeatArgument is null ? 1 : int.Parse(repeatArgument[9..]);
        if (repeats is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(repeats), "Use 1 to 10 repeats.");
        var totalFailures = 0;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = theme == "CosmicPaper" ? ThemeMode.Light : ThemeMode.Dark };
        app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(File.ReadAllText(Path.Combine(source, $"src/CosmicTranscriber/Themes/{theme}.xaml"))));
        var shared = File.ReadAllText(Path.Combine(source, "src/CosmicTranscriber/Themes/SharedStyles.xaml"))
            .Replace("clr-namespace:CosmicTranscriber.Models", "clr-namespace:CosmicTranscriber.Models;assembly=LayoutRegression");
        app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(shared));
        var document = XDocument.Load(Path.Combine(source, "src/CosmicTranscriber/MainWindow.xaml"));
        var stripped = new List<string>();
        var index = 0;
        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes().ToArray())
            {
                if (attribute.Name == X + "Class" || (attribute.Name.NamespaceName == "" && Events.Contains(attribute.Name.LocalName)))
                {
                    stripped.Add($"{element.Name.LocalName}.{attribute.Name.LocalName}={attribute.Value}");
                    attribute.Remove();
                }
            }
            if (Meaningful.Contains(element.Name.LocalName))
                element.SetAttributeValue("Uid", (string?)element.Attribute(X + "Name") ?? $"{element.Name.LocalName}-{++index:000}");
        }
        File.WriteAllText(Path.Combine(output, "stripped-handlers.json"), JsonSerializer.Serialize(stripped, JsonOptions));
        File.WriteAllText(Path.Combine(output, "isolated-layout.xaml"), document.ToString());
        var results = new List<object>();
        for (var repetition = 1; repetition <= repeats; repetition++)
        foreach (var (width, height) in diagnosticCase ? new[] { (780, 500) } : quick ? new[] { (960, 620), (640, 420) } : new[] { (1060, 700), (1044, 660), (960, 620), (944, 600), (780, 500), (640, 420) })
        foreach (var dpi in quick || diagnosticCase ? new[] { 96 } : new[] { 96, 144, 192 })
        foreach (var expanded in new[] { false, true })
        {
            var window = (Window)XamlReader.Parse(document.ToString());
            var content = (FrameworkElement)window.Content;
            ((ComboBox)window.FindName("ModelComboBox")).SelectedIndex = expanded ? 1 : 0;
            ((TextBox)window.FindName("OutputPathTextBox")).Text = "C:\\Synthetic QA\\Example output folder with a descriptive name\\Transcripts";
            if (expanded)
            {
                ((TextBlock)window.FindName("CurrentActionText")).Text = "Retrying chunk 2 of 3 after a temporary network error; saved chunks are retained.";
                ((TextBox)window.FindName("LogTextBox")).Text = "Synthetic QA only.\nChunk 1 checkpoint saved.\nRetrying chunk 2 of 3 after a temporary network error.";
            }
            window.Content = null;
            TextElement.SetFontFamily(content, window.FontFamily);
            TextElement.SetForeground(content, window.Foreground);
            TextOptions.SetTextFormattingMode(content, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(content, TextRenderingMode.ClearType);
            var surface = new Border { Width = width, Height = height, Child = content, ClipToBounds = true, UseLayoutRounding = window.UseLayoutRounding, SnapsToDevicePixels = window.SnapsToDevicePixels, Background = (Brush)app.Resources["AppBackgroundBrush"] };
            content.DataContext = new
            {
                Files = new[] { new QueueSample() }
            };
            Layout(surface, width, height);
            foreach (var expander in Traverse(content).OfType<Expander>()) expander.IsExpanded = expanded;
            Layout(surface, width, height);
            var controls = Traverse(content).OfType<FrameworkElement>().Where(e => !string.IsNullOrEmpty(e.Uid)).Distinct().ToList();
            var rectangles = controls.Select(e => Measure(e, surface)).Where(m => m is not null).Cast<ElementMeasurement>().ToList();
            var overlaps = new List<object>();
            for (var first = 0; first < rectangles.Count; first++)
            for (var second = first + 1; second < rectangles.Count; second++)
            {
                var a = rectangles[first]; var b = rectangles[second];
                if (a.Element is ListView or Expander || b.Element is ListView or Expander || IsAncestor(a.Element, b.Element) || IsAncestor(b.Element, a.Element)) continue;
                var intersection = Rect.Intersect(a.Visible, b.Visible);
                if (!intersection.IsEmpty && intersection.Width > 3 && intersection.Height > 3 && intersection.Width * intersection.Height > 60)
                    overlaps.Add(new { first = a.Id, second = b.Id, area = Math.Round(intersection.Width * intersection.Height, 2), bounds = Box(intersection) });
            }
            var prefix = $"{width}x{height}-{dpi}dpi-{(expanded ? "expanded" : "default")}";
            if (repeats > 1) prefix += $"-run{repetition}";
            Render(surface, width, height, dpi, Path.Combine(output, prefix + ".png"));
            // Ask ordinary WPF scrolling to reveal each source control, then remeasure it.
            // This is in-memory layout only: no Window.Show, input injection, HWND or OS automation.
            var reachability = new List<Reachability>();
            var revealMeasurements = new List<object>();
            var expected = document.Descendants().Where(e => e.Attribute("Uid") is not null &&
                (Meaningful.Contains(e.Name.LocalName) && e.Name.LocalName != "Expander") &&
                (string?)e.Attribute("TextTrimming") is not ("CharacterEllipsis" or "WordEllipsis") &&
                !e.AncestorsAndSelf().Any(a => (string?)a.Attribute("Visibility") is "Collapsed" or "Hidden") &&
                (expanded || !e.Ancestors().Any(a => a.Name.LocalName == "Expander"))).ToList();
            foreach (var expectedElement in expected)
            {
                var id = (string)expectedElement.Attribute("Uid")!;
                var control = controls.FirstOrDefault(c => c.Uid == id);
                if (control is null)
                {
                    reachability.Add(new Reachability(id, expectedElement.Name.LocalName, "Missing from visual tree", 0, [], [], [], "missing"));
                    continue;
                }
                if (seedDiagnosticScroll && control is Button { Content: "Open diagnostic logs" })
                {
                    // Reproduce a fractional scroll offset using normal scrolling APIs.
                    var ancestors = new List<ScrollViewer>();
                    for (var ancestor = VisualTreeHelper.GetParent(control); ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
                        if (ancestor is ScrollViewer scroll) ancestors.Add(scroll);
                    ancestors[0].ScrollToVerticalOffset(ancestors[0].ScrollableHeight / 2);
                    ancestors[^1].ScrollToVerticalOffset(0);
                    Layout(surface, width, height);
                }
                control.BringIntoView();
                Layout(surface, width, height);
                var measurement = Measure(control, surface);
                var firstMeasurement = measurement;
                var revealAttempts = 1;
                // Nested ScrollViewers can round the first reveal to a fractional-DIP
                // offset that still clips an edge. Test reachability after at most
                // three ordinary reveal requests; do not change the visibility threshold.
                while (!singleReveal && measurement is not null && measurement.VisibleFraction < 1 && revealAttempts < 3)
                {
                    control.BringIntoView();
                    Layout(surface, width, height);
                    measurement = Measure(control, surface);
                    revealAttempts++;
                }
                if (firstMeasurement is not null)
                    revealMeasurements.Add(new { id, firstMeasurement.Label, attempts = revealAttempts,
                        firstVisibleFraction = firstMeasurement.VisibleFraction, firstBounds = Box(firstMeasurement.Bounds), firstVisible = Box(firstMeasurement.Visible),
                        finalVisibleFraction = measurement?.VisibleFraction, finalBounds = measurement is null ? [] : Box(measurement.Bounds),
                        finalVisible = measurement is null ? [] : Box(measurement.Visible) });
                if (measurement is not null)
                    reachability.Add(new Reachability(measurement.Id, measurement.Type, measurement.Label, measurement.VisibleFraction, measurement.ScrollAncestors, Box(measurement.Bounds), Box(measurement.Visible), IsReachable(measurement) ? "reachable" : "clipped"));
                else
                    reachability.Add(new Reachability(id, control.GetType().Name, "Missing or zero-size", 0, [], [], [], "zero-size-or-hidden"));
                if (control.Name is "ModelComboBox" or "LogTextBox" or "StartButton")
                    Render(surface, width, height, dpi, Path.Combine(output, prefix + "-" + control.Name + ".png"));
            }
            var failures = reachability.Where(r => r.Status != "reachable").ToList();
            var start = controls.OfType<Button>().Single(b => b.Name == "StartButton");
            var buttonForegrounds = Foregrounds(start);
            start.IsEnabled = false;
            Layout(surface, width, height);
            var disabledButtonForegrounds = Foregrounds(start);
            start.IsEnabled = true;
            Layout(surface, width, height);
            var foregroundFailures = new List<string>();
            CheckForegrounds(buttonForegrounds, "#FF1B1425", "enabled", foregroundFailures);
            CheckForegrounds(disabledButtonForegrounds, app.Resources["TextMutedBrush"].ToString()!, "disabled", foregroundFailures);
            totalFailures += failures.Count + foregroundFailures.Count;
            var result = new
            {
                widthDips = width, heightDips = height, rasterDpi = dpi,
                pixelWidth = (int)Math.Ceiling(width * dpi / 96d), pixelHeight = (int)Math.Ceiling(height * dpi / 96d),
                mode = "Off-screen WPF Measure/Arrange at effective client size; RenderTargetBitmap raster density. No native window, monitor DPI change, code-behind, settings, keys or network.",
                scenario = expanded ? "expanded-log-long-model-and-status" : "default-synthetic-queue",
                layoutDpi = VisualTreeHelper.GetDpi(surface).PixelsPerDip * 96,
                useLayoutRounding = surface.UseLayoutRounding, snapsToDevicePixels = surface.SnapsToDevicePixels,
                controls = rectangles.Select(m => new { m.Id, m.Type, m.Label, bounds = Box(m.Bounds), visible = Box(m.Visible), m.VisibleFraction, m.ScrollAncestors }),
                reachabilityTolerance = "At least 98% of bounds visible, or no edge clipped by more than 1 DIP (WPF rounding tolerance). Missing/zero-size always fails.",
                seedDiagnosticScroll, initialViewportOverlaps = overlaps, revealMeasurements, reachability, failures, buttonForegrounds, disabledButtonForegrounds, foregroundFailures
            };
            File.WriteAllText(Path.Combine(output, prefix + ".json"), JsonSerializer.Serialize(result, JsonOptions));
            results.Add(new { prefix, controls = rectangles.Count, initialClipped = rectangles.Count(m => !IsReachable(m)), initialViewportOverlaps = overlaps.Count, expectedElements = expected.Count, unreachable = failures.Count, foregroundFailures = foregroundFailures.Count, canvas = Traverse(content).OfType<FrameworkElement>().Where(e => e.Name == "WorkspaceCanvas").Select(e => new { e.ActualWidth, e.ActualHeight }), viewport = Traverse(content).OfType<ScrollViewer>().Where(e => e.Name == "WorkspaceViewport").Select(e => new { e.ViewportWidth, e.ViewportHeight, e.ExtentWidth, e.ExtentHeight }) });
            Console.WriteLine($"Rendered {prefix}; measured {rectangles.Count} controls, {overlaps.Count} overlap candidates.");
            surface.Child = null;
            window.Close();
        }
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(results, JsonOptions));
        app.Shutdown();
        Console.WriteLine($"LAYOUT {(totalFailures == 0 ? "PASSED" : "FINDINGS")}: {totalFailures} reachability or button foreground failures across {results.Count} scenarios. Assertion mode: {assertReachable}.");
        return assertReachable && totalFailures > 0 ? 1 : 0;
    }
    private static void Layout(FrameworkElement surface, double width, double height)
    {
        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
        surface.UpdateLayout();
        surface.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        surface.UpdateLayout();
    }
    private static IEnumerable<DependencyObject> Traverse(DependencyObject element)
    {
        yield return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            foreach (var child in Traverse(VisualTreeHelper.GetChild(element, i))) yield return child;
    }
    private static bool LocallyVisible(FrameworkElement element, Visual root)
    {
        DependencyObject? current = element;
        while (current is not null && current != root)
        {
            if (current is UIElement ui && ui.Visibility != Visibility.Visible) return false;
            current = VisualTreeHelper.GetParent(current);
        }
        return true;
    }
    private static bool IsAncestor(DependencyObject parent, DependencyObject child)
    {
        for (var current = VisualTreeHelper.GetParent(child); current is not null; current = VisualTreeHelper.GetParent(current))
            if (current == parent) return true;
        return false;
    }
    private static ElementMeasurement? Measure(FrameworkElement element, FrameworkElement root)
    {
        if (!LocallyVisible(element, root) || element.ActualWidth <= 0 || element.ActualHeight <= 0) return null;
        var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        var visible = Rect.Intersect(bounds, new Rect(root.RenderSize));
        var scrolls = new List<string>();
        for (DependencyObject? current = element; current is not null && current != root; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ScrollViewer sv) scrolls.Add($"{sv.Name}: {sv.ViewportWidth:0.#}x{sv.ViewportHeight:0.#}, extent {sv.ExtentWidth:0.#}x{sv.ExtentHeight:0.#}");
            if (current is Visual visual)
            {
                var clip = VisualTreeHelper.GetClip(visual);
                if (clip is not null) visible.Intersect(visual.TransformToAncestor(root).TransformBounds(clip.Bounds));
                if (current is UIElement { ClipToBounds: true } clipped) visible.Intersect(visual.TransformToAncestor(root).TransformBounds(new Rect(clipped.RenderSize)));
                if (current is ScrollContentPresenter presenter) visible.Intersect(visual.TransformToAncestor(root).TransformBounds(new Rect(presenter.RenderSize)));
            }
        }
        var label = element switch { TextBlock t => t.Text, ContentControl c => c.Content?.ToString(), TextBox t => t.Text, _ => element.Name };
        return new ElementMeasurement(element, element.Uid, element.GetType().Name, (label ?? "")[..Math.Min(80, (label ?? "").Length)], bounds, visible,
            visible.IsEmpty ? 0 : Math.Round(visible.Width * visible.Height / (bounds.Width * bounds.Height), 4), scrolls);
    }
    private static double[] Box(Rect rectangle) => rectangle.IsEmpty ? [] : [Math.Round(rectangle.X, 2), Math.Round(rectangle.Y, 2), Math.Round(rectangle.Width, 2), Math.Round(rectangle.Height, 2)];
    private static bool IsReachable(ElementMeasurement measurement) => !measurement.Visible.IsEmpty &&
        (measurement.VisibleFraction >= .98 ||
         (measurement.Visible.Left - measurement.Bounds.Left <= 1 && measurement.Visible.Top - measurement.Bounds.Top <= 1 &&
          measurement.Bounds.Right - measurement.Visible.Right <= 1 && measurement.Bounds.Bottom - measurement.Visible.Bottom <= 1));
    private static List<ForegroundMeasurement> Foregrounds(Button button) => Traverse(button).OfType<FrameworkElement>()
        .Where(e => e is Button or TextBlock or AccessText or ContentPresenter).Select(e =>
        {
            var property = e is Control ? Control.ForegroundProperty : TextElement.ForegroundProperty;
            var source = DependencyPropertyHelper.GetValueSource(e, property);
            return new ForegroundMeasurement(e.GetType().Name, e.GetValue(property)?.ToString() ?? "", source.BaseValueSource.ToString(), source.IsExpression);
        }).ToList();
    private static void CheckForegrounds(List<ForegroundMeasurement> values, string expected, string state, List<string> failures)
    {
        if (!values.Any(v => v.Type is "TextBlock" or "AccessText")) failures.Add($"{state}: button text is missing.");
        foreach (var value in values.Where(v => v.Value != expected)) failures.Add($"{state}: {value.Type} foreground {value.Value}, expected {expected}.");
    }
    private static void Render(Visual surface, double width, double height, int dpi, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi / 96d), (int)Math.Ceiling(height * dpi / 96d), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
    private sealed record ElementMeasurement(FrameworkElement Element, string Id, string Type, string Label, Rect Bounds, Rect Visible, double VisibleFraction, List<string> ScrollAncestors);
    private sealed record Reachability(string Id, string Type, string Label, double VisibleFraction, List<string> ScrollAncestors, double[] Bounds, double[] Visible, string Status);
    private sealed record ForegroundMeasurement(string Type, string Value, string ValueSource, bool IsExpression);
    public sealed class QueueSample
    {
        public string FileName { get; set; } = "Synthetic sample.mp3";
        public string SourcePath { get; set; } = "C:\\Synthetic QA\\sample.mp3";
        public string MetadataText { get; set; } = "10:50  ·  2.5 MB";
        public string Detail { get; set; } = "Ready";
        public AudioJobStatus Status { get; set; } = AudioJobStatus.Ready;
        public string StatusText { get; set; } = "READY";
        public double Progress { get; set; }
    }
}

