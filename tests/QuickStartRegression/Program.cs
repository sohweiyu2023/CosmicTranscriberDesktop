using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CosmicTranscriber;
using CosmicTranscriber.Models;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string label)
    { if (!condition) throw new Exception("FAIL " + label); _checks++; Console.WriteLine("PASS " + label); }
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--interactive")
        {
            // Interactive fixture: real MainWindow, disposable settings, no inherited provider key.
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            var profile = Path.GetFullPath(args[1]);
            var marker = Path.Combine(profile, "tutorial-qa-profile.marker");
            if (Directory.Exists(profile) && Directory.EnumerateFileSystemEntries(profile).Any()
                && (!File.Exists(marker) || File.ReadAllText(marker) != "Cosmic tutorial QA disposable profile"))
                throw new InvalidOperationException("Use an empty directory or a previously marked tutorial QA profile.");
            if (File.Exists(Path.Combine(profile, "openai-key.protected")))
                throw new InvalidOperationException("The QA profile must not contain a saved API key.");
            Directory.CreateDirectory(profile);
            File.WriteAllText(marker, "Cosmic tutorial QA disposable profile");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var settingsService = Activator.CreateInstance(typeof(CosmicTranscriber.Services.AppSettingsService), flags, null, new object[] { profile }, null)!;
            var realApp = new App { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var palette = new ResourceDictionary { Source = new Uri("pack://application:,,,/CosmicTranscriber;component/Themes/CosmicNight.xaml") };
            realApp.Resources.MergedDictionaries.Add(palette);
            realApp.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/CosmicTranscriber;component/Themes/SharedStyles.xaml") });
            typeof(App).GetField("_paletteDictionary", flags)!.SetValue(realApp, palette);
            var mainWindow = (MainWindow)Activator.CreateInstance(typeof(MainWindow), flags, null, new[] { settingsService }, null)!;
            mainWindow.Title = "Cosmic tutorial QA - disposable profile";
            realApp.Run(mainWindow);
            return;
        }
        var root = Path.GetFullPath(args[0]);
        var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        var guide = new QuickStartGuide();
        Check(QuickStartGuide.Steps.Count == 7, "welcome and six workflow steps");
        guide.Back(); Check(guide.Index == 0 && !guide.CanGoBack, "back bounded at first step");
        for (var i = 0; i < 30; i++) guide.Next();
        Check(guide.Index == 6 && guide.IsLast, "next bounded at final step");
        for (var i = 0; i < 30; i++) guide.Back();
        Check(guide.Index == 0 && !guide.IsLast, "repeated back bounded");
        Check(new QuickStartGuide().Index == 0, "reopen starts at welcome");
        Check(QuickStartGuide.Steps.All(x => x.Title.Length > 0 && x.Body.Length > 0 && x.Tip.Length > 0), "all steps have readable content");
        var text = string.Join(" ", QuickStartGuide.Steps.Select(x => x.Body + " " + x.Tip));
        foreach (var word in new[] { "no API requests", "M4A", "FFmpeg", "ffprobe", "charged", "not a spending cap", "queue is not saved", "not verified identities" })
            Check(text.Contains(word, StringComparison.Ordinal), "honest guidance: " + word);
        Check(!new AppSettings().QuickStartInvitationDismissed, "old/default profiles show optional invitation");
        var settings = new AppSettings { QuickStartInvitationDismissed = true, Model = "gpt-transcribe", OutputDirectory = "synthetic" };
        var roundtrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Check(roundtrip.QuickStartInvitationDismissed && roundtrip.OutputDirectory == "synthetic", "dismissal round-trips without replacing unrelated fields");
        Check(!JsonSerializer.Deserialize<AppSettings>("{}")!.QuickStartInvitationDismissed, "legacy missing field stays compatible");
        var main = File.ReadAllText(Path.Combine(root, "src/CosmicTranscriber/MainWindow.xaml.cs"));
        Check(main.Contains("guide.Show(); // Modeless"), "modeless tutorial does not trap Cancel");
        Check(main.Contains("_quickStartWindow is not null") && main.Contains("guide.Closed += (_, _) => _quickStartWindow = null"), "single instance and reopen guard");
        var show = main.Split("private void ShowQuickStartArea(string area)")[1].Split("private void About_Click")[0];
        Check(!show.Contains("Start_Click") && !show.Contains("TestKey") && !show.Contains("ApiPasswordBox"), "show area never runs action or reads key");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var theme in new[] { "CosmicNight", "CosmicPaper" })
        {
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/CosmicTranscriber;component/Themes/{theme}.xaml") });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/CosmicTranscriber;component/Themes/SharedStyles.xaml") });
            foreach (var width in new[] { 340.0, 610.0 })
            {
                string? area = null;
                var window = new QuickStartWindow(value => area = value);
                var title = (TextBlock)window.FindName("StepTitleText");
                var next = (Button)window.FindName("NextButton");
                var back = (Button)window.FindName("BackButton");
                var showArea = (Button)window.FindName("ShowAreaButton");
                Check(!back.IsEnabled, $"{theme}/{width} initial back disabled");
                for (var i = 0; i < 7; i++)
                {
                    Check(title.Text == QuickStartGuide.Steps[i].Title, $"{theme}/{width} actual XAML step {i}");
                    showArea.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(area == QuickStartGuide.Steps[i].Area, "show area callback maps step");
                    var content = (FrameworkElement)window.Content;
                    content.Measure(new Size(width, 440)); content.Arrange(new Rect(0, 0, width, 440)); content.UpdateLayout();
                    Check(next.ActualWidth > 0 && next.ActualHeight > 0 && next.IsEnabled, "navigation remains measurable");
                    if (i is 0 or 5)
                    {
                        var bitmap = new RenderTargetBitmap((int)width, 440, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(output, $"{theme}-{width}-step{i}.png")); encoder.Save(stream);
                    }
                    if (i < 6) next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                Check((string)next.Content == "Finish", "final button label");
                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(title.Text == QuickStartGuide.Steps[5].Title, "actual back handler");
                bool closed = false;
                window.Closed += (_, _) => closed = true;
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(closed, "Finish invokes actual close handler");
            }
        }
        var closeWindow = new QuickStartWindow();
        bool closeClicked = false;
        closeWindow.Closed += (_, _) => closeClicked = true;
        ((Button)closeWindow.FindName("CloseGuideButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(closeClicked, "Close guide button closes actual window");
        Check(main.Contains("SaveSettingsQuietly(); // Capture pending valid edits"), "dismissal captures pending edits");
        Check(main.Contains("_quickStartWindow.WindowState = WindowState.Normal"), "reopen restores minimized guide");
        Console.WriteLine($"{_checks} quick-start checks passed; no provider requests. Rendering-only: native input and owner lifecycle remain acceptance checks.");
    }
}
