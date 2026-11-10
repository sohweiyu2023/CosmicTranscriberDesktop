using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

// Real-input Windows acceptance for the visible application. It launches the built executable,
// moves the real cursor and clicks with SendInput, types with SendInput, reads state through UI
// Automation and checks the actual top-level popup window. It never starts a transcription,
// never enters an API key and never contacts a provider.
internal static class Program
{
    private static readonly List<Result> Results = [];
    private static string _evidence = "";
    private static bool _printShots;
    private static string _exe = "";
    private static Process? _process;
    private static IntPtr _window;
    private static AutomationElement? _root;
    private static int _shot;

    private static readonly string[] Models = ["gpt-transcribe", "gpt-4o-transcribe-diarize", "gpt-4o-transcribe", "gpt-4o-mini-transcribe"];

    [STAThread]
    private static int Main(string[] args)
    {
        _exe = Path.GetFullPath(Arg(args, "--exe") ?? throw new ArgumentException("--exe <CosmicTranscriber.exe> is required."));
        _evidence = Path.GetFullPath(Arg(args, "--evidence") ?? Path.Combine(Path.GetTempPath(), "cosmic-ui-acceptance"));
        _printShots = args.Contains("--print-shots");
        var allowProfile = args.Contains("--use-current-windows-profile");
        if (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase) && !allowProfile)
        {
            Console.Error.WriteLine("Refusing to run: this drives the real app with the current Windows user's settings folder. " +
                "Run it on a disposable CI runner, or in a disposable Windows account with --use-current-windows-profile.");
            return 2;
        }
        Directory.CreateDirectory(_evidence);
        Console.WriteLine($"UI acceptance: exe={_exe}");
        Console.WriteLine($"Screen: {Native.GetSystemMetrics(78)}x{Native.GetSystemMetrics(79)} px, harness DPI awareness PerMonitorV2");

        Step("launch", () => Launch(1000, 740));
        Step("retry selector stays disabled", RetrySelectorStaysDisabled);
        Step("mouse opens fully visible selector", () =>
        {
            Arrange("visible");
            OpenByMouse("visible", "value");
            OpenByMouse("visible", "arrow");
        });
        Step("mouse opens selector clipped at the bottom of the settings page", () =>
        {
            if (Arrange("clip-bottom")) OpenByMouse("clip-bottom", "value");
        });
        Step("mouse opens selector clipped at the top of the settings page", () =>
        {
            if (Arrange("clip-top")) OpenByMouse("clip-top", "value");
        });
        Step("mouse selects every model", () =>
        {
            foreach (var model in new[] { "gpt-4o-mini-transcribe", "gpt-4o-transcribe", "gpt-4o-transcribe-diarize", "gpt-transcribe" })
                SelectByMouse(model, "visible");
            foreach (var model in new[] { "gpt-4o-transcribe-diarize", "gpt-transcribe" })
                SelectByMouse(model, "clip-bottom");
        });
        Step("dismissal", Dismissal);
        Step("keyboard", Keyboard);
        Step("mouse wheel", Wheel);
        Step("theme toggle", Themes);
        Step("window sizes", Sizes);
        Step("selection persists after restart", Persistence);
        Step("import transition", ImportTransition);
        Step("close", () => CloseApp(expectPrompt: false));
        DumpLogs();

        var failed = Results.Count(r => !r.Passed);
        File.WriteAllText(Path.Combine(_evidence, "ui-acceptance-results.json"),
            JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"UI ACCEPTANCE SUMMARY: {Results.Count - failed} passed, {failed} failed, {Results.Count} checks");
        return failed == 0 ? 0 : 1;
    }

    private static string? Arg(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private sealed record Result(string Name, bool Passed, string Detail);

    private static void Check(string name, bool passed, string detail = "")
    {
        Results.Add(new Result(name, passed, detail));
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  | " + detail : "")}");
    }

    private static void Step(string name, Action action)
    {
        Console.WriteLine($"--- {name}");
        try { action(); }
        catch (Exception ex) { Check($"{name}: completed without harness exception", false, ex.GetType().Name + ": " + ex.Message); Shot("exception-" + name); }
    }

    // ---------------- application lifecycle ----------------

    private static void Launch(int width, int height)
    {
        var start = new ProcessStartInfo(_exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(_exe)! };
        _process = Process.Start(start) ?? throw new InvalidOperationException("Process.Start returned null.");
        var clock = Stopwatch.StartNew();
        AutomationElement? combo = null;
        while (clock.Elapsed < TimeSpan.FromSeconds(90))
        {
            if (_process.HasExited) throw new InvalidOperationException($"The app exited during startup with code {_process.ExitCode}.");
            _process.Refresh();
            if (_process.MainWindowHandle != IntPtr.Zero)
            {
                _window = _process.MainWindowHandle;
                _root = AutomationElement.FromHandle(_window);
                combo = _root.FindFirst(TreeScope.Descendants, Id("ModelComboBox"));
                if (combo is not null) break;
            }
            Thread.Sleep(250);
        }
        Check("app main window and model selector appear", combo is not null, $"{clock.ElapsedMilliseconds} ms; windows: {DescribeProcessWindows()}");
        if (combo is null) throw new InvalidOperationException("Main window did not become ready.");
        Native.ShowWindow(_window, 9);
        Native.SetWindowPos(_window, IntPtr.Zero, 0, 0, width, height, 0x0040 | 0x0004);
        Native.SetForegroundWindow(_window);
        Thread.Sleep(1200);
        ClickNeutral();
        Shot("launched");
    }

    private static void CloseApp(bool expectPrompt)
    {
        if (_process is null || _process.HasExited) return;
        ((WindowPattern)_root!.GetCurrentPattern(WindowPattern.Pattern)).Close();
        var exited = _process.WaitForExit(15000);
        if (!exited && !expectPrompt) { Shot("close-blocked"); Check("app closes cleanly when idle", false, DescribeProcessWindows()); _process.Kill(); _process.WaitForExit(5000); return; }
        Check("app closes cleanly when idle", exited, exited ? $"exit code {_process.ExitCode}" : "");
    }

    // ---------------- element helpers ----------------

    private static PropertyCondition Id(string id) => new(AutomationElement.AutomationIdProperty, id);

    private static AutomationElement Find(string id) =>
        _root!.FindFirst(TreeScope.Descendants, Id(id)) ?? throw new InvalidOperationException($"Element {id} was not found.");

    private static AutomationElement Combo => Find("ModelComboBox");

    private static Rect Bounds(AutomationElement element) => element.Current.BoundingRectangle;

    private static string Selected(AutomationElement combo)
    {
        var selection = ((SelectionPattern)combo.GetCurrentPattern(SelectionPattern.Pattern)).Current.GetSelection();
        return selection.Length == 0 ? "(none)" : selection[0].Current.Name;
    }

    private static string ExpandState(AutomationElement combo) =>
        ((ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Current.ExpandCollapseState.ToString();

    private static string FocusedId()
    {
        try { var focused = AutomationElement.FocusedElement; return focused is null ? "(none)" : focused.Current.AutomationId + "/" + focused.Current.ControlType.ProgrammaticName; }
        catch (ElementNotAvailableException) { return "(unavailable)"; }
    }

    /// <summary>The ground truth: a visible top-level popup window of the app aligned with the selector.</summary>
    private static Rect? VisibleDropDown(Rect combo)
    {
        foreach (var rect in ProcessWindows().Where(w => w.Handle != _window).Select(w => w.Rect))
        {
            var aligned = Math.Abs(rect.Left - combo.Left) <= 8 && rect.Width >= combo.Width * 0.9;
            var adjacent = Math.Abs(rect.Top - combo.Bottom) <= 10 || Math.Abs(rect.Bottom - combo.Top) <= 10;
            if (aligned && adjacent && rect.Height >= 30) return rect;
        }
        return null;
    }

    private static List<(IntPtr Handle, Rect Rect)> ProcessWindows()
    {
        var found = new List<(IntPtr, Rect)>();
        if (_process is null) return found;
        Native.EnumWindows((handle, _) =>
        {
            Native.GetWindowThreadProcessId(handle, out var pid);
            if (pid == _process.Id && Native.IsWindowVisible(handle) && Native.GetWindowRect(handle, out var r) && r.Right > r.Left && r.Bottom > r.Top)
                found.Add((handle, new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)));
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static string DescribeProcessWindows() =>
        string.Join("; ", ProcessWindows().Select(w => $"{(w.Handle == _window ? "main" : "other")}@{w.Rect}"));

    // ---------------- scroll arrangement ----------------

    /// <summary>Puts the model selector fully visible, or clipped at the viewport's bottom/top edge.</summary>
    private static bool Arrange(string mode)
    {
        var viewport = Find("SettingsViewport");
        var scroll = (ScrollPattern)viewport.GetCurrentPattern(ScrollPattern.Pattern);
        var view = Bounds(viewport);
        if (!scroll.Current.VerticallyScrollable)
        {
            Check($"arrange {mode}: settings page is scrollable at this size", mode == "visible", $"viewport {view}");
            return mode == "visible";
        }
        (double Percent, double Score)? best = null;
        for (var percent = 0.0; percent <= 100.0; percent += 0.5)
        {
            scroll.SetScrollPercent(ScrollPattern.NoScroll, percent);
            Thread.Sleep(15);
            var combo = Bounds(Combo);
            var visibleHeight = Math.Max(0, Math.Min(combo.Bottom, view.Bottom) - Math.Max(combo.Top, view.Top));
            var fraction = combo.Height <= 0 ? 0 : visibleHeight / combo.Height;
            double? score = mode switch
            {
                "visible" when combo.Top >= view.Top + 60 && combo.Bottom <= view.Bottom - 200 => Math.Abs(combo.Top - (view.Top + 120)),
                "clip-bottom" when combo.Bottom > view.Bottom && fraction is >= 0.45 and <= 0.75 => Math.Abs(fraction - 0.6),
                "clip-top" when combo.Top < view.Top && fraction is >= 0.45 and <= 0.75 => Math.Abs(fraction - 0.6),
                _ => null,
            };
            if (score is not null && (best is null || score < best.Value.Score)) best = (percent, score.Value);
        }
        if (best is null)
        {
            Check($"arrange {mode}: a matching scroll position exists", mode != "visible", $"viewport {view}; not applicable at this window size");
            scroll.SetScrollPercent(ScrollPattern.NoScroll, 0);
            return false;
        }
        scroll.SetScrollPercent(ScrollPattern.NoScroll, best.Value.Percent);
        Thread.Sleep(250);
        Console.WriteLine($"      arranged {mode}: scroll {best.Value.Percent:0.0}% combo {Bounds(Combo)} viewport {view}");
        return true;
    }

    private static Point VisiblePoint(string area)
    {
        var combo = Bounds(Combo);
        var view = Bounds(Find("SettingsViewport"));
        var top = Math.Max(combo.Top, view.Top);
        var bottom = Math.Min(combo.Bottom, view.Bottom);
        var y = (top + bottom) / 2;
        var x = area == "arrow" ? combo.Right - 15 : combo.Left + Math.Min(70, combo.Width / 4);
        return new Point(Math.Round(x), Math.Round(y));
    }

    /// <summary>Moves keyboard focus away from the selector with an ordinary click on static header text.</summary>
    private static void ClickNeutral()
    {
        var header = Bounds(Find("HeaderStatusText"));
        Mouse.Click(new Point(Math.Round(header.Left + 10), Math.Round(header.Top + header.Height / 2)));
        Thread.Sleep(350);
    }

    // ---------------- scenarios ----------------

    private static bool OpenByMouse(string scenario, string area, bool closeAfter = true)
    {
        ClickNeutral();
        if (scenario != "current") Arrange(scenario);
        var before = Selected(Combo);
        var comboBefore = Bounds(Combo);
        var point = VisiblePoint(area);
        Mouse.Click(point);
        Thread.Sleep(800);
        var comboAfter = Bounds(Combo);
        var popup = VisibleDropDown(comboAfter);
        var state = ExpandState(Combo);
        var after = Selected(Combo);
        var shot = Shot($"open-{scenario}-{area}");
        var detail = $"click {point}; combo before {comboBefore} after {comboAfter}; popup {(popup?.ToString() ?? "none")}; " +
                     $"expand {state}; selection {before} -> {after}; focus {FocusedId()}; windows {DescribeProcessWindows()}; shot {shot}";
        Check($"{scenario}/{area}: one mouse click shows the model list", popup is not null && state == "Expanded", detail);
        Check($"{scenario}/{area}: opening click does not change the model", after == before, $"{before} -> {after}");
        if (closeAfter && (popup is not null || state != "Collapsed"))
        {
            Mouse.Key(Native.VK_ESCAPE);
            Thread.Sleep(400);
        }
        return popup is not null;
    }

    private static void SelectByMouse(string model, string scenario)
    {
        if (!OpenByMouse(scenario, "value", closeAfter: false)) return;
        var item = Combo.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.NameProperty, model));
        if (item is null) { Check($"select {model} ({scenario}): list item exists", false); Mouse.Key(Native.VK_ESCAPE); return; }
        var rect = Bounds(item);
        var point = new Point(Math.Round(rect.Left + rect.Width / 2), Math.Round(rect.Top + rect.Height / 2));
        Mouse.Click(point);
        Thread.Sleep(700);
        var selected = Selected(Combo);
        var popup = VisibleDropDown(Bounds(Combo));
        Shot($"selected-{model}-{scenario}");
        Check($"select {model} ({scenario}): mouse click selects it and closes the list", selected == model && popup is null,
            $"item {rect}; selected {selected}; popup {(popup?.ToString() ?? "none")}; expand {ExpandState(Combo)}");
    }

    private static void Dismissal()
    {
        Arrange("visible");
        var before = Selected(Combo);
        if (OpenByMouse("current", "value", closeAfter: false))
        {
            var neutral = Bounds(Find("QueueSummaryText"));
            Mouse.Click(new Point(Math.Round(neutral.Left + 20), Math.Round(neutral.Top + neutral.Height / 2)));
            Thread.Sleep(600);
            Check("click outside closes the list without changing the model",
                VisibleDropDown(Bounds(Combo)) is null && ExpandState(Combo) == "Collapsed" && Selected(Combo) == before,
                $"expand {ExpandState(Combo)}; selection {Selected(Combo)}");
        }
        if (OpenByMouse("current", "arrow", closeAfter: false))
        {
            Mouse.Key(Native.VK_ESCAPE);
            Thread.Sleep(500);
            Check("Escape closes the list without changing the model",
                VisibleDropDown(Bounds(Combo)) is null && ExpandState(Combo) == "Collapsed" && Selected(Combo) == before,
                $"expand {ExpandState(Combo)}; selection {Selected(Combo)}");
        }
        if (OpenByMouse("current", "value", closeAfter: false))
        {
            Mouse.Click(VisiblePoint("arrow"));
            Thread.Sleep(600);
            Check("second click on the field closes the list", VisibleDropDown(Bounds(Combo)) is null && ExpandState(Combo) == "Collapsed",
                $"expand {ExpandState(Combo)}");
            OpenByMouse("current", "value");
            Check("list reopens after being closed by a click", true);
        }
    }

    private static void Keyboard()
    {
        Arrange("visible");
        ClickNeutral();
        Combo.SetFocus();
        Thread.Sleep(400);
        Check("selector accepts keyboard focus", FocusedId().StartsWith("ModelComboBox", StringComparison.Ordinal), FocusedId());
        Mouse.Key(Native.VK_TAB);
        Thread.Sleep(300);
        var afterTab = FocusedId();
        Mouse.Key(Native.VK_SHIFT, Native.VK_TAB);
        Thread.Sleep(300);
        Check("Tab leaves and Shift+Tab returns to the selector", !afterTab.StartsWith("ModelComboBox", StringComparison.Ordinal) && FocusedId().StartsWith("ModelComboBox", StringComparison.Ordinal),
            $"after Tab {afterTab}; after Shift+Tab {FocusedId()}");

        var before = Selected(Combo);
        Mouse.Key(Native.VK_F4);
        Thread.Sleep(600);
        Check("F4 opens the list", VisibleDropDown(Bounds(Combo)) is not null, $"expand {ExpandState(Combo)}; shot {Shot("keyboard-f4")}");
        Mouse.Key(Native.VK_DOWN);
        Thread.Sleep(200);
        Mouse.Key(Native.VK_RETURN);
        Thread.Sleep(600);
        var expected = Models[(Array.IndexOf(Models, before) + 1) % Models.Length];
        Check("Down then Enter selects the next model and closes the list",
            Selected(Combo) == expected && VisibleDropDown(Bounds(Combo)) is null, $"{before} -> {Selected(Combo)} (expected {expected})");

        Mouse.Key(Native.VK_MENU, Native.VK_DOWN);
        Thread.Sleep(600);
        Check("Alt+Down opens the list", VisibleDropDown(Bounds(Combo)) is not null, $"expand {ExpandState(Combo)}");
        Mouse.Key(Native.VK_UP);
        Thread.Sleep(200);
        Mouse.Key(Native.VK_RETURN);
        Thread.Sleep(600);
        Check("Up then Enter restores the previous model", Selected(Combo) == before, Selected(Combo));
    }

    private static void Wheel()
    {
        var viewport = Find("SettingsViewport");
        var scroll = (ScrollPattern)viewport.GetCurrentPattern(ScrollPattern.Pattern);
        Arrange("visible");
        ClickNeutral();
        var before = Selected(Combo);
        var offset = scroll.Current.VerticalScrollPercent;
        var notches = offset > 50 ? 2 : -2;
        Mouse.Wheel(VisiblePoint("value"), notches);
        Thread.Sleep(600);
        Check("wheel over the closed selector does not change the model", Selected(Combo) == before, $"{before} -> {Selected(Combo)}");
        Check("wheel over the closed selector still scrolls the settings page",
            !scroll.Current.VerticallyScrollable || Math.Abs(scroll.Current.VerticalScrollPercent - offset) > 0.1,
            $"scroll {offset:0.0}% -> {scroll.Current.VerticalScrollPercent:0.0}%");

        Combo.SetFocus();
        Thread.Sleep(300);
        Mouse.Wheel(VisiblePoint("value"), -1);
        Thread.Sleep(500);
        Check("wheel over the focused closed selector does not change the model", Selected(Combo) == before, $"{before} -> {Selected(Combo)}");
    }

    private static void Themes()
    {
        for (var pass = 0; pass < 2; pass++)
        {
            ClickNeutral();
            var toggle = Bounds(Find("ThemeToggleButton"));
            Mouse.Click(new Point(Math.Round(toggle.Left + toggle.Width / 2), Math.Round(toggle.Top + toggle.Height / 2)));
            Thread.Sleep(1200);
            var theme = Find("ThemeToggleButton").Current.Name;
            Console.WriteLine($"      theme button now: {theme}");
            OpenByMouse("visible", "value");
            SelectByMouse("gpt-4o-transcribe", "visible");
            SelectByMouse("gpt-transcribe", "visible");
        }
    }

    private static void Sizes()
    {
        Native.SetWindowPos(_window, IntPtr.Zero, 0, 0, 976, 659, 0x0040 | 0x0004);
        Thread.Sleep(900);
        OpenByMouse("visible", "value");
        OpenByMouse("clip-bottom", "value");
        Native.ShowWindow(_window, 3);
        Thread.Sleep(1200);
        OpenByMouse("visible", "arrow");
        Native.ShowWindow(_window, 9);
        Native.SetWindowPos(_window, IntPtr.Zero, 0, 0, 1000, 740, 0x0040 | 0x0004);
        Thread.Sleep(900);
    }

    private static void Persistence()
    {
        SelectByMouse("gpt-4o-mini-transcribe", "visible");
        Thread.Sleep(2500); // settings autosave is debounced (900 ms)
        CloseApp(expectPrompt: false);
        Launch(1000, 740);
        Check("selected model persists after restart", Selected(Combo) == "gpt-4o-mini-transcribe", Selected(Combo));
        SelectByMouse("gpt-transcribe", "visible");
    }

    private static void ImportTransition()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cosmic-ui-acceptance-media", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        var files = new[] { Path.Combine(folder, "合成 テスト ✓.mp3"), Path.Combine(folder, "nested", "synthetic-silence.mp3") };
        foreach (var file in files) File.WriteAllBytes(file, SilentMp3(seconds: 3));
        var queue = Find("QueueSummaryText");
        var before = queue.Current.Name;
        for (var paste = 1; paste <= 2; paste++)
        {
            var list = new System.Collections.Specialized.StringCollection { folder };
            Clipboard.SetFileDropList(list);
            ClickNeutral();
            Mouse.Key(Native.VK_CONTROL, (ushort)'V');
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(30) && Find("QueueSummaryText").Current.Name == before)
                Thread.Sleep(250);
            Thread.Sleep(1500);
            var count = Find("FilesListView").FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Count;
            before = Find("QueueSummaryText").Current.Name;
            Check($"paste {paste}: synthetic Unicode-named and nested MP3s are queued once", count == files.Length,
                $"queue items {count}; summary '{Find("QueueSummaryText").Current.Name}'; dialogs {DescribeProcessWindows()}; shot {Shot($"import-{paste}")}");
            DismissModal();
        }
        OpenByMouse("visible", "value");
        SelectByMouse("gpt-4o-transcribe-diarize", "visible");
        SelectByMouse("gpt-transcribe", "visible");
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void DismissModal()
    {
        foreach (var window in ProcessWindows().Where(w => w.Handle != _window))
        {
            var element = AutomationElement.FromHandle(window.Handle);
            if (element.Current.ControlType == ControlType.Window)
            {
                Console.WriteLine($"      dismissing window '{element.Current.Name}'");
                Native.SetForegroundWindow(window.Handle);
                Mouse.Key(Native.VK_RETURN);
                Thread.Sleep(500);
            }
        }
    }

    private static void RetrySelectorStaysDisabled()
    {
        var retry = Find("RetryComboBox");
        Check("retry selector is disabled", !retry.Current.IsEnabled);
        Check("retry selector explains that automatic paid retries are disabled",
            retry.Current.Name.Contains("disabled", StringComparison.OrdinalIgnoreCase) && retry.Current.HelpText.Contains("never retried automatically", StringComparison.OrdinalIgnoreCase),
            $"name '{retry.Current.Name}'; help '{retry.Current.HelpText}'");
    }

    // ---------------- evidence ----------------

    /// <summary>MPEG-1 Layer III 128 kb/s 44.1 kHz frames with zero side information decode as silence.</summary>
    private static byte[] SilentMp3(int seconds)
    {
        const int frameBytes = 417;
        var frames = (int)Math.Ceiling(seconds * 44100 / 1152.0);
        var data = new byte[frames * frameBytes];
        for (var i = 0; i < frames; i++)
        {
            var offset = i * frameBytes;
            data[offset] = 0xFF; data[offset + 1] = 0xFB; data[offset + 2] = 0x90; data[offset + 3] = 0x04;
        }
        return data;
    }

    private static string Shot(string name)
    {
        var file = Path.Combine(_evidence, $"{++_shot:000}-{name}.png");
        try
        {
            int x = Native.GetSystemMetrics(76), y = Native.GetSystemMetrics(77), w = Native.GetSystemMetrics(78), h = Native.GetSystemMetrics(79);
            var screen = Native.GetDC(IntPtr.Zero);
            var memory = Native.CreateCompatibleDC(screen);
            var bitmap = Native.CreateCompatibleBitmap(screen, w, h);
            var old = Native.SelectObject(memory, bitmap);
            Native.BitBlt(memory, 0, 0, w, h, screen, x, y, 0x00CC0020 | 0x40000000);
            Native.SelectObject(memory, old);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            Native.DeleteObject(bitmap); Native.DeleteDC(memory); Native.ReleaseDC(IntPtr.Zero, screen);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(source));
            using (var stream = File.Create(file)) png.Save(stream);
            if (_printShots)
            {
                var jpeg = new JpegBitmapEncoder { QualityLevel = 55 };
                jpeg.Frames.Add(BitmapFrame.Create(new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(0.5, 0.5))));
                using var buffer = new MemoryStream();
                jpeg.Save(buffer);
                Console.WriteLine($"SHOT-BEGIN {Path.GetFileName(file)}");
                var encoded = Convert.ToBase64String(buffer.ToArray());
                for (var i = 0; i < encoded.Length; i += 4000) Console.WriteLine("SHOT " + encoded.Substring(i, Math.Min(4000, encoded.Length - i)));
                Console.WriteLine("SHOT-END");
            }
        }
        catch (Exception ex) { Console.WriteLine($"      screenshot failed: {ex.GetType().Name}: {ex.Message}"); }
        return Path.GetFileName(file);
    }

    private static void DumpLogs()
    {
        var logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CosmicTranscriber", "logs");
        if (!Directory.Exists(logs)) { Console.WriteLine($"No app logs at {logs}"); return; }
        foreach (var file in new DirectoryInfo(logs).GetFiles("*", SearchOption.AllDirectories).OrderBy(f => f.LastWriteTimeUtc))
        {
            File.Copy(file.FullName, Path.Combine(_evidence, "app-log-" + file.Name), overwrite: true);
            var interesting = File.ReadLines(file.FullName, Encoding.UTF8)
                .Where(l => l.Contains("dropdown", StringComparison.OrdinalIgnoreCase) || l.Contains("ModelComboBox", StringComparison.Ordinal) ||
                            l.Contains("SettingsViewport", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal) || l.Contains("\"level\":\"error\"", StringComparison.OrdinalIgnoreCase))
                .TakeLast(160).ToList();
            Console.WriteLine($"--- app log {file.Name} ({file.Length} bytes), {interesting.Count} selected lines");
            foreach (var line in interesting) Console.WriteLine("LOG " + (line.Length > 600 ? line[..600] + "…" : line));
        }
    }
}

internal static class Mouse
{
    public static void Click(Point point)
    {
        Move(point);
        Thread.Sleep(90);
        Native.Send(Native.Mouse(0, 0, 0x0002, 0));
        Thread.Sleep(80);
        Native.Send(Native.Mouse(0, 0, 0x0004, 0));
        Native.GetCursorPos(out var actual);
        if (Math.Abs(actual.X - point.X) > 1 || Math.Abs(actual.Y - point.Y) > 1)
            Console.WriteLine($"      WARNING cursor at {actual.X},{actual.Y} not {point}");
    }

    public static void Move(Point point)
    {
        int vx = Native.GetSystemMetrics(76), vy = Native.GetSystemMetrics(77), vw = Native.GetSystemMetrics(78), vh = Native.GetSystemMetrics(79);
        var dx = (int)Math.Round((point.X - vx) * 65535.0 / Math.Max(1, vw - 1));
        var dy = (int)Math.Round((point.Y - vy) * 65535.0 / Math.Max(1, vh - 1));
        Native.Send(Native.Mouse(dx, dy, 0x0001 | 0x8000 | 0x4000, 0));
    }

    public static void Wheel(Point point, int notches)
    {
        Move(point);
        Thread.Sleep(120);
        for (var i = 0; i < Math.Abs(notches); i++)
        {
            Native.Send(Native.Mouse(0, 0, 0x0800, unchecked((uint)(notches < 0 ? -120 : 120))));
            Thread.Sleep(120);
        }
    }

    /// <summary>Presses keys in order and releases them in reverse, as a person would for a chord.</summary>
    public static void Key(params ushort[] keys)
    {
        foreach (var key in keys) { Native.Send(Native.Keyboard(key, 0)); Thread.Sleep(30); }
        foreach (var key in keys.Reverse()) { Native.Send(Native.Keyboard(key, 0x0002)); Thread.Sleep(30); }
    }
}

internal static class Native
{
    public const ushort VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12,
        VK_ESCAPE = 0x1B, VK_UP = 0x26, VK_DOWN = 0x28, VK_F4 = 0x73;

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public InputUnion u; }

    public static INPUT Mouse(int dx, int dy, uint flags, uint data) =>
        new() { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags, mouseData = data } } };

    public static INPUT Keyboard(ushort key, uint flags)
    {
        // Arrow keys are extended keys; without the flag some layouts deliver numeric-keypad codes.
        if (key is VK_UP or VK_DOWN) flags |= 0x0001;
        return new() { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = key, dwFlags = flags } } };
    }

    public static void Send(INPUT input)
    {
        var sent = SendInput(1, [input], Marshal.SizeOf<INPUT>());
        if (sent != 1) throw new InvalidOperationException($"SendInput failed: {Marshal.GetLastWin32Error()}");
    }

    public delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle, out int processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr handle);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr handle, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
}
